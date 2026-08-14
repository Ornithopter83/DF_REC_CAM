const jsonHeaders = {
  "content-type": "application/json; charset=utf-8",
};

type JsonObject = Record<string, unknown>;

Deno.serve(async (request) => {
  try {
    const corsResponse = handleCors(request);
    if (corsResponse) return corsResponse;

    const publishableKey = request.headers.get("apikey")?.trim() ?? "";
    if (!isConfiguredKey(publishableKey, "SUPABASE_PUBLISHABLE_KEYS", "sb_publishable_")) {
      return json({ error_code: "invalid_publishable_key" }, 401, request);
    }

    const route = getRoute(request.url);
    const catalog = route.match(/^devices\/([0-9a-f-]{36})\/recordings\/catalog$/i);
    if (request.method === "POST" && catalog) {
      return await registerCatalog(catalog[1], request);
    }

    if (request.method === "GET" && route === "portal/cameras") {
      return await listPortalCameras(request, publishableKey);
    }

    if (request.method === "POST" && route === "nas-session") {
      return await createNasSessions(request, publishableKey);
    }

    const cameraRecordings = route.match(/^cameras\/([0-9a-f-]{36})\/recordings$/i);
    if (request.method === "GET" && cameraRecordings) {
      return await listRecordings(cameraRecordings[1], request, publishableKey);
    }

    const downloadUrl = route.match(/^recordings\/([0-9a-f-]{36})\/download-url$/i);
    if (request.method === "POST" && downloadUrl) {
      return await createDownloadUrl(downloadUrl[1], request, publishableKey);
    }

    return json({ error_code: "route_not_found" }, 404, request);
  } catch (error) {
    if (error instanceof RequestError) {
      return json({ error_code: error.code }, error.status, request);
    }
    return json({ error_code: "internal_error" }, 500, request);
  }
});

async function registerCatalog(deviceId: string, request: Request): Promise<Response> {
  const deviceToken = bearerToken(request);
  const body = await readJson(request);
  const recording = firstRow(await adminRpc("register_device_recording_catalog", {
    p_device_id: deviceId,
    p_device_token: deviceToken,
    p_camera_id: requiredUuid(body, "camera_id"),
    p_nas_relative_path: requiredRelativePath(body, "nas_relative_path"),
    p_original_file_name: requiredMp4FileName(body, "original_file_name"),
    p_file_size_bytes: requiredInteger(body, "file_size_bytes", 1, 10_995_116_277_760),
    p_source_fingerprint: requiredSha256(body, "source_fingerprint"),
    p_recorded_at: requiredDateTime(body, "recorded_at"),
    p_duration_seconds: optionalNumber(body, "duration_seconds", 0, 604_800),
    p_source_last_modified_at: optionalDateTime(body, "source_last_modified_at"),
  }));
  return json({
    recording_id: recording.recording_id,
    catalog_state: recording.catalog_state,
  }, 200, request);
}

async function listPortalCameras(
  request: Request,
  publishableKey: string,
): Promise<Response> {
  const userJwt = bearerToken(request);
  const value = await userRpc("list_portal_cameras", {}, publishableKey, userJwt);
  if (!Array.isArray(value)) throw new RequestError("invalid_database_response", 502);
  return json({
    items: value.map((entry) => {
      const row = objectValue(entry);
      return {
        id: row.camera_id,
        display_name: row.display_name,
        camera_type: row.camera_type,
        connection_state: row.connection_state,
        storage_state: row.storage_state,
        device_id: row.device_id,
        device_last_seen_at: row.device_last_seen_at,
        device_public_ip: row.device_public_ip,
        heartbeat_interval_seconds: row.heartbeat_interval_seconds,
        device_online: Boolean(row.device_online),
      };
    }),
  }, 200, request);
}

async function createNasSessions(
  request: Request,
  publishableKey: string,
): Promise<Response> {
  const userJwt = bearerToken(request);
  const value = await userRpc("get_user_nas_session_scopes", {}, publishableKey, userJwt);
  if (!Array.isArray(value)) throw new RequestError("invalid_database_response", 502);

  const issuedAt = Math.floor(Date.now() / 1000);
  const assertionExpiresAt = issuedAt + 120;
  const sessionExpiresAt = issuedAt + (8 * 60 * 60);
  const sessions = [];
  for (const entry of value) {
    const row = objectValue(entry);
    const userId = String(row.user_id ?? "");
    if (!isUuid(userId) || !Array.isArray(row.scopes)) {
      throw new RequestError("invalid_database_response", 502);
    }
    const gatewayBaseUrl = validateGatewayBaseUrl(String(row.gateway_base_url ?? ""));
    const scopes = row.scopes.map(validateNasScope);
    if (scopes.length === 0) continue;
    const assertion = await signNasAssertion({
      v: 1,
      iss: "dfblackbox-recording-media",
      aud: "dfblackbox-nas-gateway",
      sub: userId,
      iat: issuedAt,
      exp: assertionExpiresAt,
      session_exp: sessionExpiresAt,
      jti: crypto.randomUUID(),
      gateway_base_url: gatewayBaseUrl,
      scopes,
    });
    sessions.push({
      gateway_base_url: gatewayBaseUrl,
      exchange_url: new URL("auth.php", gatewayBaseUrl).toString(),
      logout_url: new URL("logout.php", gatewayBaseUrl).toString(),
      session_expires_at: new Date(sessionExpiresAt * 1000).toISOString(),
      assertion,
    });
  }
  return json({ sessions }, 200, request);
}

async function listRecordings(
  cameraId: string,
  request: Request,
  publishableKey: string,
): Promise<Response> {
  const userJwt = bearerToken(request);
  const url = new URL(request.url);
  const limit = queryInteger(url.searchParams.get("limit"), 50, 1, 100);
  const cursor = decodeCursor(url.searchParams.get("cursor"));
  const value = await userRpc("list_camera_recordings", {
    p_camera_id: cameraId,
    p_limit: limit + 1,
    p_cursor_recorded_at: cursor?.recordedAt ?? null,
    p_cursor_id: cursor?.id ?? null,
  }, publishableKey, userJwt);
  if (!Array.isArray(value)) throw new RequestError("invalid_database_response", 502);

  const hasMore = value.length > limit;
  const rows = value.slice(0, limit).map((entry) => {
    const row = objectValue(entry);
    return {
      id: row.recording_id,
      camera_id: row.camera_id,
      original_file_name: row.original_file_name,
      recorded_at: row.recorded_at,
      duration_seconds: row.duration_seconds,
      file_size_bytes: row.file_size_bytes,
    };
  });
  const last = hasMore ? rows.at(-1) : null;
  return json({
    items: rows,
    next_cursor: last ? encodeCursor(String(last.recorded_at), String(last.id)) : null,
  }, 200, request);
}

async function createDownloadUrl(
  recordingId: string,
  request: Request,
  publishableKey: string,
): Promise<Response> {
  const userJwt = bearerToken(request);
  const value = await userRpc("get_recording_nas_access", {
    p_recording_id: recordingId,
  }, publishableKey, userJwt);
  if (!Array.isArray(value) || value.length === 0) {
    throw new RequestError("recording_not_found", 404);
  }
  const recording = firstRow(value);
  const downloadUrl = buildGatewayDownloadUrl(
    String(recording.gateway_base_url),
    String(recording.recording_id),
    String(recording.nas_location_id),
    String(recording.nas_relative_path),
  );
  return json({
    download_url: downloadUrl,
    file_name: recording.original_file_name,
    authentication_required: false,
  }, 200, request);
}

function buildGatewayDownloadUrl(
  baseValue: string,
  recordingId: string,
  locationId: string,
  relativePath: string,
): string {
  const baseValueNormalized = validateGatewayBaseUrl(baseValue);
  if (!isUuid(recordingId) || !isUuid(locationId)) {
    throw new RequestError("invalid_nas_location", 502);
  }
  if (
    relativePath.startsWith("/") || relativePath.includes("\\") || relativePath.includes(":") ||
    relativePath.includes("//") || !relativePath.toLowerCase().endsWith(".mp4") ||
    relativePath.split("/").some((segment) => !segment || segment === "." || segment === "..")
  ) {
    throw new RequestError("invalid_nas_location", 502);
  }
  const result = new URL("download.php", baseValueNormalized);
  result.searchParams.set("recording", recordingId);
  result.searchParams.set("location", locationId);
  result.searchParams.set("path", relativePath);
  return result.toString();
}

function validateGatewayBaseUrl(baseValue: string): string {
  const base = new URL(baseValue);
  if (
    base.protocol !== "https:" || base.username || base.password || base.search || base.hash ||
    !base.pathname.endsWith("/")
  ) {
    throw new RequestError("invalid_nas_location", 502);
  }
  return base.toString();
}

function validateNasScope(value: unknown): { location_id: string; prefixes: string[] } {
  const scope = objectValue(value);
  const locationId = String(scope.location_id ?? "");
  if (!isUuid(locationId) || !Array.isArray(scope.prefixes)) {
    throw new RequestError("invalid_database_response", 502);
  }
  const prefixes = scope.prefixes.map((entry) => {
    if (typeof entry !== "string") throw new RequestError("invalid_database_response", 502);
    const prefix = entry.trim().replace(/^\/+|\/+$/g, "");
    if (
      !prefix || prefix.length > 768 || prefix.includes("\\") || prefix.includes(":") ||
      prefix.includes("//") || prefix.split("/").some((segment) => !segment || segment === "." || segment === "..")
    ) {
      throw new RequestError("invalid_database_response", 502);
    }
    return prefix;
  });
  return { location_id: locationId, prefixes: [...new Set(prefixes)] };
}

async function adminRpc(name: string, body: JsonObject): Promise<unknown> {
  return await rpc(name, body, { apikey: serviceSecret() });
}

async function userRpc(
  name: string,
  body: JsonObject,
  publishableKey: string,
  userJwt: string,
): Promise<unknown> {
  return await rpc(name, body, {
    apikey: publishableKey,
    authorization: `Bearer ${userJwt}`,
  });
}

async function rpc(name: string, body: JsonObject, headers: Record<string, string>): Promise<unknown> {
  const response = await fetch(`${requiredEnvironment("SUPABASE_URL")}/rest/v1/rpc/${name}`, {
    method: "POST",
    headers: { ...jsonHeaders, ...headers },
    body: JSON.stringify(body),
  });
  if (!response.ok) {
    const databaseError = await readDatabaseError(response);
    if (databaseError === "device_authentication_failed") {
      throw new RequestError(databaseError, 401);
    }
    if (databaseError === "camera_not_found" || databaseError === "recording_not_found") {
      throw new RequestError(databaseError, 404);
    }
    if (databaseError === "nas_location_not_configured") {
      throw new RequestError(databaseError, 409);
    }
    if (databaseError.endsWith("_denied") || databaseError === "authentication_required") {
      throw new RequestError("operation_not_authorized", 403);
    }
    if (databaseError === "fingerprint_metadata_conflict" || databaseError.endsWith("_mismatch")) {
      throw new RequestError(databaseError, 409);
    }
    if (databaseError.startsWith("invalid_")) {
      throw new RequestError(databaseError, 400);
    }
    const status = response.status === 401 || response.status === 403 ? 403 : 502;
    throw new RequestError(status === 403 ? "operation_not_authorized" : "database_operation_failed", status);
  }
  if (response.status === 204) return null;
  return await response.json();
}

async function readDatabaseError(response: Response): Promise<string> {
  try {
    const value = objectValue(await response.json());
    const message = typeof value.message === "string" ? value.message.trim() : "";
    return message.match(/^[a-z][a-z0-9_]{1,79}$/)?.[0] ?? "";
  } catch {
    return "";
  }
}

async function readJson(request: Request): Promise<JsonObject> {
  try {
    const raw = await request.text();
    if (new TextEncoder().encode(raw).byteLength > 8192) {
      throw new RequestError("request_too_large", 413);
    }
    return objectValue(JSON.parse(raw));
  } catch (error) {
    if (error instanceof RequestError) throw error;
    throw new RequestError("invalid_json", 400);
  }
}

function requiredUuid(body: JsonObject, key: string): string {
  const value = requiredString(body, key, 36);
  if (!isUuid(value)) throw new RequestError(`invalid_${key}`, 400);
  return value;
}

function requiredSha256(body: JsonObject, key: string): string {
  const value = requiredString(body, key, 64).toLowerCase();
  if (!/^[0-9a-f]{64}$/.test(value)) throw new RequestError(`invalid_${key}`, 400);
  return value;
}

function requiredRelativePath(body: JsonObject, key: string): string {
  const value = requiredString(body, key, 1024);
  if (
    value.startsWith("/") || value.includes("\\") || value.includes(":") || value.includes("//") ||
    value.split("/").some((segment) => segment === "." || segment === ".." || !segment)
  ) {
    throw new RequestError(`invalid_${key}`, 400);
  }
  return value;
}

function requiredMp4FileName(body: JsonObject, key: string): string {
  const value = requiredString(body, key, 255);
  if (!value.toLowerCase().endsWith(".mp4") || value.includes("/") || value.includes("\\")) {
    throw new RequestError(`invalid_${key}`, 400);
  }
  return value;
}

function requiredDateTime(body: JsonObject, key: string): string {
  const value = requiredString(body, key, 64);
  if (!Number.isFinite(Date.parse(value))) throw new RequestError(`invalid_${key}`, 400);
  return value;
}

function optionalDateTime(body: JsonObject, key: string): string | null {
  if (body[key] === null || body[key] === undefined || body[key] === "") return null;
  return requiredDateTime(body, key);
}

function requiredInteger(
  body: JsonObject,
  key: string,
  minimum: number,
  maximum: number,
): number {
  const value = body[key];
  if (typeof value !== "number" || !Number.isSafeInteger(value) || value < minimum || value > maximum) {
    throw new RequestError(`invalid_${key}`, 400);
  }
  return value;
}

function optionalNumber(body: JsonObject, key: string, minimum: number, maximum: number): number | null {
  const value = body[key];
  if (value === null || value === undefined || value === "") return null;
  if (typeof value !== "number" || !Number.isFinite(value) || value < minimum || value > maximum) {
    throw new RequestError(`invalid_${key}`, 400);
  }
  return value;
}

function requiredString(body: JsonObject, key: string, maxLength: number): string {
  const value = body[key];
  if (typeof value !== "string" || !value.trim() || value.trim().length > maxLength) {
    throw new RequestError(`invalid_${key}`, 400);
  }
  return value.trim();
}

function queryInteger(raw: string | null, fallback: number, minimum: number, maximum: number): number {
  if (raw === null || raw === "") return fallback;
  if (!/^\d+$/.test(raw)) throw new RequestError("invalid_limit", 400);
  const value = Number(raw);
  if (!Number.isSafeInteger(value) || value < minimum || value > maximum) {
    throw new RequestError("invalid_limit", 400);
  }
  return value;
}

function bearerToken(request: Request): string {
  const authorization = request.headers.get("authorization") ?? "";
  const match = authorization.match(/^Bearer\s+(.+)$/i);
  if (!match || !match[1].trim()) throw new RequestError("authorization_required", 401);
  return match[1].trim();
}

function firstRow(value: unknown): JsonObject {
  if (!Array.isArray(value) || value.length !== 1) {
    throw new RequestError("invalid_database_response", 502);
  }
  return objectValue(value[0]);
}

function objectValue(value: unknown): JsonObject {
  if (!value || typeof value !== "object" || Array.isArray(value)) {
    throw new RequestError("invalid_database_response", 502);
  }
  return value as JsonObject;
}

function isUuid(value: string): boolean {
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(value);
}

function encodeCursor(recordedAt: string, id: string): string {
  return btoa(`${recordedAt}|${id}`).replaceAll("+", "-").replaceAll("/", "_").replace(/=+$/, "");
}

function decodeCursor(raw: string | null): { recordedAt: string; id: string } | null {
  if (!raw) return null;
  if (!/^[A-Za-z0-9_-]{1,256}$/.test(raw)) throw new RequestError("invalid_cursor", 400);
  try {
    const base64 = raw.replaceAll("-", "+").replaceAll("_", "/").padEnd(Math.ceil(raw.length / 4) * 4, "=");
    const [recordedAt, id, extra] = atob(base64).split("|");
    if (extra !== undefined || !recordedAt || !Number.isFinite(Date.parse(recordedAt)) || !isUuid(id)) {
      throw new Error();
    }
    return { recordedAt, id };
  } catch {
    throw new RequestError("invalid_cursor", 400);
  }
}

function requiredEnvironment(name: string): string {
  const value = Deno.env.get(name)?.trim();
  if (!value) throw new RequestError("server_not_configured", 503);
  return value;
}

function serviceSecret(): string {
  return configuredKey("DFBLACKBOX_SUPABASE_SECRET_KEY", "SUPABASE_SECRET_KEYS", "sb_secret_");
}

let nasSigningKeyPromise: Promise<CryptoKey> | null = null;

async function signNasAssertion(payload: JsonObject): Promise<string> {
  const header = encodeBase64Url(new TextEncoder().encode(JSON.stringify({ alg: "RS256", typ: "JWT" })));
  const body = encodeBase64Url(new TextEncoder().encode(JSON.stringify(payload)));
  const signingInput = `${header}.${body}`;
  const signature = await crypto.subtle.sign(
    "RSASSA-PKCS1-v1_5",
    await nasSigningKey(),
    new TextEncoder().encode(signingInput),
  );
  return `${signingInput}.${encodeBase64Url(new Uint8Array(signature))}`;
}

function nasSigningKey(): Promise<CryptoKey> {
  if (nasSigningKeyPromise) return nasSigningKeyPromise;
  const pem = requiredEnvironment("NAS_SESSION_PRIVATE_KEY")
    .replace(/\\n/g, "\n")
    .trim();
  const match = pem.match(/-----BEGIN PRIVATE KEY-----([\s\S]+)-----END PRIVATE KEY-----/);
  if (!match) throw new RequestError("nas_signing_key_not_configured", 503);
  const binary = atob(match[1].replace(/\s/g, ""));
  const bytes = Uint8Array.from(binary, (character) => character.charCodeAt(0));
  nasSigningKeyPromise = crypto.subtle.importKey(
    "pkcs8",
    bytes,
    { name: "RSASSA-PKCS1-v1_5", hash: "SHA-256" },
    false,
    ["sign"],
  ).catch(() => {
    nasSigningKeyPromise = null;
    throw new RequestError("nas_signing_key_not_configured", 503);
  });
  return nasSigningKeyPromise;
}

function encodeBase64Url(bytes: Uint8Array): string {
  let binary = "";
  for (let offset = 0; offset < bytes.length; offset += 0x8000) {
    binary += String.fromCharCode(...bytes.subarray(offset, offset + 0x8000));
  }
  return btoa(binary).replaceAll("+", "-").replaceAll("/", "_").replace(/=+$/, "");
}

function getRoute(rawUrl: string): string {
  const pathname = new URL(rawUrl).pathname;
  const marker = "/recording-media";
  const markerIndex = pathname.indexOf(marker);
  return markerIndex < 0 ? "" : pathname.slice(markerIndex + marker.length).replace(/^\/+|\/+$/g, "");
}

function configuredKey(directName: string, dictionaryName: string, prefix: string): string {
  const direct = Deno.env.get(directName)?.trim();
  if (direct?.startsWith(prefix)) return direct;
  const keys = configuredKeys(dictionaryName, prefix);
  if (keys.length === 0) throw new RequestError("server_key_not_configured", 503);
  return keys[0];
}

function isConfiguredKey(candidate: string, dictionaryName: string, prefix: string): boolean {
  return candidate.startsWith(prefix) && configuredKeys(dictionaryName, prefix).includes(candidate);
}

function configuredKeys(name: string, prefix: string): string[] {
  const raw = Deno.env.get(name);
  if (!raw) return [];
  try {
    return collectStrings(JSON.parse(raw)).filter((value) => value.startsWith(prefix));
  } catch {
    return [];
  }
}

function collectStrings(value: unknown): string[] {
  if (typeof value === "string") return [value];
  if (Array.isArray(value)) return value.flatMap(collectStrings);
  if (value && typeof value === "object") return Object.values(value).flatMap(collectStrings);
  return [];
}

function handleCors(request: Request): Response | null {
  if (request.method !== "OPTIONS") return null;
  const origin = allowedOrigin(request);
  if (!origin) return new Response(null, { status: 403 });
  return new Response(null, {
    status: 204,
    headers: {
      "access-control-allow-origin": origin,
      "access-control-allow-headers": "authorization, apikey, content-type",
      "access-control-allow-methods": "GET, POST, OPTIONS",
      "access-control-max-age": "600",
      vary: "origin",
    },
  });
}

function allowedOrigin(request: Request): string | null {
  const configured = Deno.env.get("APPROVAL_ALLOWED_ORIGIN")?.trim();
  const requested = request.headers.get("origin")?.trim();
  return configured && requested === configured ? configured : null;
}

function json(value: JsonObject, status: number, request: Request): Response {
  const headers: Record<string, string> = { ...jsonHeaders };
  const origin = allowedOrigin(request);
  if (origin) {
    headers["access-control-allow-origin"] = origin;
    headers.vary = "origin";
  }
  return new Response(JSON.stringify(value), { status, headers });
}

class RequestError extends Error {
  readonly code: string;
  readonly status: number;

  constructor(code: string, status: number) {
    super(code);
    this.code = code;
    this.status = status;
  }
}
