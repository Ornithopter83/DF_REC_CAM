import { createClient } from "npm:@supabase/supabase-js@2.111.0";

const RECORDING_BUCKET = "dfblackbox-recordings";
const SIGNED_URL_LIFETIME_SECONDS = 300;
const TUS_CHUNK_SIZE_BYTES = 6 * 1024 * 1024;
const jsonHeaders = {
  "content-type": "application/json; charset=utf-8",
};

type JsonObject = Record<string, unknown>;

Deno.serve(async (request) => {
  try {
    const corsResponse = handleCors(request);
    if (corsResponse) return corsResponse;

    const publishableKey = request.headers.get("apikey")?.trim() ?? "";
    if (
      !isConfiguredKey(
        publishableKey,
        "SUPABASE_PUBLISHABLE_KEYS",
        "sb_publishable_",
      )
    ) {
      return json({ error_code: "invalid_publishable_key" }, 401, request);
    }

    const route = getRoute(request.url);
    const uploadSession = route.match(
      /^devices\/([0-9a-f-]{36})\/recordings\/upload-session$/i,
    );
    if (request.method === "POST" && uploadSession) {
      return await createUploadSession(uploadSession[1], request);
    }

    const uploadComplete = route.match(
      /^devices\/([0-9a-f-]{36})\/recordings\/([0-9a-f-]{36})\/complete$/i,
    );
    if (request.method === "POST" && uploadComplete) {
      return await completeUpload(
        uploadComplete[1],
        uploadComplete[2],
        request,
      );
    }

    const cameraRecordings = route.match(
      /^cameras\/([0-9a-f-]{36})\/recordings$/i,
    );
    if (request.method === "GET" && cameraRecordings) {
      return await listRecordings(cameraRecordings[1], request, publishableKey);
    }

    const playUrl = route.match(/^recordings\/([0-9a-f-]{36})\/play-url$/i);
    if (request.method === "POST" && playUrl) {
      return await createMediaUrl(playUrl[1], false, request, publishableKey);
    }

    const downloadUrl = route.match(
      /^recordings\/([0-9a-f-]{36})\/download-url$/i,
    );
    if (request.method === "POST" && downloadUrl) {
      return await createMediaUrl(
        downloadUrl[1],
        true,
        request,
        publishableKey,
      );
    }

    return json({ error_code: "route_not_found" }, 404, request);
  } catch (error) {
    if (error instanceof RequestError) {
      return json({ error_code: error.code }, error.status, request);
    }
    return json({ error_code: "internal_error" }, 500, request);
  }
});

async function createUploadSession(
  deviceId: string,
  request: Request,
): Promise<Response> {
  const deviceToken = bearerToken(request);
  const body = await readJson(request);
  const fileSizeBytes = requiredInteger(
    body,
    "file_size_bytes",
    1,
    10_995_116_277_760,
  );
  const sourceFingerprint = requiredSha256(body, "source_fingerprint");

  const rows = await adminRpc("begin_device_recording_upload", {
    p_device_id: deviceId,
    p_device_token: deviceToken,
    p_camera_id: requiredUuid(body, "camera_id"),
    p_source_relative_path: requiredRelativePath(body, "source_relative_path"),
    p_original_file_name: requiredMp4FileName(body, "original_file_name"),
    p_file_size_bytes: fileSizeBytes,
    p_source_fingerprint: sourceFingerprint,
    p_recorded_at: requiredDateTime(body, "recorded_at"),
    p_duration_seconds: optionalNumber(body, "duration_seconds", 0, 604_800),
    p_source_last_modified_at: optionalDateTime(
      body,
      "source_last_modified_at",
    ),
  });
  const recording = firstRow(rows);
  const recordingId = String(recording.recording_id);
  const bucket = String(recording.storage_bucket);
  const objectPath = String(recording.storage_object_path);

  if (recording.sync_state === "ready") {
    return json(
      {
        recording_id: recordingId,
        sync_state: "ready",
        upload_required: false,
      },
      200,
      request,
    );
  }

  const existingSize = await storageObjectSize(bucket, objectPath);
  if (existingSize !== null) {
    if (existingSize !== fileSizeBytes) {
      throw new RequestError("storage_object_conflict", 409);
    }
    const completed = firstRow(
      await adminRpc("complete_device_recording_upload", {
        p_device_id: deviceId,
        p_device_token: deviceToken,
        p_recording_id: recordingId,
        p_file_size_bytes: fileSizeBytes,
        p_source_fingerprint: sourceFingerprint,
      }),
    );
    return json(
      {
        recording_id: completed.recording_id,
        sync_state: completed.sync_state,
        upload_required: false,
        uploaded_at: completed.uploaded_at,
      },
      200,
      request,
    );
  }

  const storage = adminStorage();
  const { data, error } = await storage.from(bucket).createSignedUploadUrl(
    objectPath,
    {
      upsert: false,
    },
  );
  if (error || !data?.token) {
    throw new RequestError("upload_signature_failed", 502);
  }

  return json(
    {
      recording_id: recordingId,
      sync_state: "uploading",
      upload_required: true,
      bucket,
      object_path: objectPath,
      tus: {
        endpoint: resumableStorageEndpoint(),
        signature: data.token,
        signature_expires_at: new Date(Date.now() + 2 * 60 * 60 * 1000)
          .toISOString(),
        chunk_size_bytes: TUS_CHUNK_SIZE_BYTES,
        metadata: {
          bucketName: bucket,
          objectName: objectPath,
          contentType: "video/mp4",
          cacheControl: "3600",
        },
      },
    },
    200,
    request,
  );
}

async function completeUpload(
  deviceId: string,
  recordingId: string,
  request: Request,
): Promise<Response> {
  const deviceToken = bearerToken(request);
  const body = await readJson(request);
  const fileSizeBytes = requiredInteger(
    body,
    "file_size_bytes",
    1,
    10_995_116_277_760,
  );
  const sourceFingerprint = requiredSha256(body, "source_fingerprint");

  const recording = firstRow(
    await adminRpc("get_device_recording_upload", {
      p_device_id: deviceId,
      p_device_token: deviceToken,
      p_recording_id: recordingId,
      p_file_size_bytes: fileSizeBytes,
      p_source_fingerprint: sourceFingerprint,
    }),
  );
  if (recording.sync_state === "ready") {
    return json(
      {
        recording_id: recording.recording_id,
        sync_state: "ready",
        uploaded_at: recording.uploaded_at,
      },
      200,
      request,
    );
  }

  const uploadedSize = await storageObjectSize(
    String(recording.storage_bucket),
    String(recording.storage_object_path),
  );
  if (uploadedSize === null) throw new RequestError("upload_not_complete", 409);
  if (uploadedSize !== fileSizeBytes) {
    throw new RequestError("uploaded_size_mismatch", 409);
  }

  const completed = firstRow(
    await adminRpc("complete_device_recording_upload", {
      p_device_id: deviceId,
      p_device_token: deviceToken,
      p_recording_id: recordingId,
      p_file_size_bytes: fileSizeBytes,
      p_source_fingerprint: sourceFingerprint,
    }),
  );
  return json(
    {
      recording_id: completed.recording_id,
      sync_state: completed.sync_state,
      uploaded_at: completed.uploaded_at,
    },
    200,
    request,
  );
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
  const value = await userRpc(
    "list_camera_recordings",
    {
      p_camera_id: cameraId,
      p_limit: limit + 1,
      p_cursor_recorded_at: cursor?.recordedAt ?? null,
      p_cursor_id: cursor?.id ?? null,
    },
    publishableKey,
    userJwt,
  );
  if (!Array.isArray(value)) {
    throw new RequestError("invalid_database_response", 502);
  }

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
  return json(
    {
      items: rows,
      next_cursor: last
        ? encodeCursor(String(last.recorded_at), String(last.id))
        : null,
    },
    200,
    request,
  );
}

async function createMediaUrl(
  recordingId: string,
  download: boolean,
  request: Request,
  publishableKey: string,
): Promise<Response> {
  const userJwt = bearerToken(request);
  const value = await userRpc(
    "get_recording_media_access",
    {
      p_recording_id: recordingId,
    },
    publishableKey,
    userJwt,
  );
  if (!Array.isArray(value) || value.length === 0) {
    throw new RequestError("recording_not_found", 404);
  }
  const recording = firstRow(value);
  const fileName = String(recording.original_file_name);
  const { data, error } = await adminStorage()
    .from(String(recording.storage_bucket))
    .createSignedUrl(
      String(recording.storage_object_path),
      SIGNED_URL_LIFETIME_SECONDS,
      download ? { download: fileName } : undefined,
    );
  if (error || !data?.signedUrl) {
    throw new RequestError("media_url_failed", 502);
  }

  const response: JsonObject = {
    signed_url: data.signedUrl,
    expires_at: new Date(Date.now() + SIGNED_URL_LIFETIME_SECONDS * 1000)
      .toISOString(),
  };
  if (download) response.file_name = fileName;
  return json(response, 200, request);
}

async function storageObjectSize(
  bucket: string,
  objectPath: string,
): Promise<number | null> {
  const { data, error } = await adminStorage().from(bucket).info(objectPath);
  if (error) {
    const status = Number(
      (error as unknown as { statusCode?: number | string }).statusCode ?? 0,
    );
    if (status === 400 || status === 404) return null;
    throw new RequestError("storage_inspection_failed", 502);
  }
  const size = Number((data as unknown as { size?: number | string }).size);
  if (!Number.isSafeInteger(size) || size < 0) {
    throw new RequestError("invalid_storage_response", 502);
  }
  return size;
}

function adminStorage() {
  return createClient(requiredEnvironment("SUPABASE_URL"), serviceSecret(), {
    auth: {
      persistSession: false,
      autoRefreshToken: false,
      detectSessionInUrl: false,
    },
  }).storage;
}

function resumableStorageEndpoint(): string {
  const url = new URL(requiredEnvironment("SUPABASE_URL"));
  const match = url.hostname.match(/^([a-z0-9]+)\.supabase\.co$/i);
  const origin = match ? `https://${match[1]}.storage.supabase.co` : url.origin;
  return `${origin}/storage/v1/upload/resumable`;
}

async function adminRpc(name: string, body: JsonObject): Promise<unknown> {
  const secret = serviceSecret();
  return await rpc(name, body, {
    apikey: secret,
  });
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

async function rpc(
  name: string,
  body: JsonObject,
  headers: Record<string, string>,
): Promise<unknown> {
  const response = await fetch(
    `${requiredEnvironment("SUPABASE_URL")}/rest/v1/rpc/${name}`,
    {
      method: "POST",
      headers: { ...jsonHeaders, ...headers },
      body: JSON.stringify(body),
    },
  );
  if (!response.ok) {
    const databaseError = await readDatabaseError(response);
    if (databaseError === "device_authentication_failed") {
      throw new RequestError(databaseError, 401);
    }
    if (
      databaseError === "camera_not_found" ||
      databaseError === "recording_not_found"
    ) {
      throw new RequestError(databaseError, 404);
    }
    if (
      databaseError.endsWith("_denied") ||
      databaseError === "authentication_required"
    ) {
      throw new RequestError("operation_not_authorized", 403);
    }
    if (
      databaseError === "fingerprint_metadata_conflict" ||
      databaseError.endsWith("_mismatch")
    ) {
      throw new RequestError(databaseError, 409);
    }
    if (databaseError.startsWith("invalid_")) {
      throw new RequestError(databaseError, 400);
    }
    const status = response.status === 401 || response.status === 403
      ? 403
      : 502;
    throw new RequestError(
      status === 403 ? "operation_not_authorized" : "database_operation_failed",
      status,
    );
  }
  if (response.status === 204) return null;
  return await response.json();
}

async function readDatabaseError(response: Response): Promise<string> {
  try {
    const value = objectValue(await response.json());
    const message = typeof value.message === "string"
      ? value.message.trim()
      : "";
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
    const value = JSON.parse(raw);
    return objectValue(value);
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
  if (!/^[0-9a-f]{64}$/.test(value)) {
    throw new RequestError(`invalid_${key}`, 400);
  }
  return value;
}

function requiredRelativePath(body: JsonObject, key: string): string {
  const value = requiredString(body, key, 1024);
  if (
    value.startsWith("/") || value.includes("\\") || value.includes(":") ||
    value.includes("//") ||
    value.split("/").some((segment) => segment === ".." || !segment)
  ) {
    throw new RequestError(`invalid_${key}`, 400);
  }
  return value;
}

function requiredMp4FileName(body: JsonObject, key: string): string {
  const value = requiredString(body, key, 255);
  if (
    !value.toLowerCase().endsWith(".mp4") || value.includes("/") ||
    value.includes("\\")
  ) {
    throw new RequestError(`invalid_${key}`, 400);
  }
  return value;
}

function requiredDateTime(body: JsonObject, key: string): string {
  const value = requiredString(body, key, 64);
  if (!Number.isFinite(Date.parse(value))) {
    throw new RequestError(`invalid_${key}`, 400);
  }
  return value;
}

function optionalDateTime(body: JsonObject, key: string): string | null {
  if (body[key] === null || body[key] === undefined || body[key] === "") {
    return null;
  }
  return requiredDateTime(body, key);
}

function requiredInteger(
  body: JsonObject,
  key: string,
  minimum: number,
  maximum: number,
): number {
  const value = body[key];
  if (
    typeof value !== "number" || !Number.isSafeInteger(value) ||
    value < minimum || value > maximum
  ) {
    throw new RequestError(`invalid_${key}`, 400);
  }
  return value;
}

function optionalNumber(
  body: JsonObject,
  key: string,
  minimum: number,
  maximum: number,
): number | null {
  const value = body[key];
  if (value === null || value === undefined || value === "") return null;
  if (
    typeof value !== "number" || !Number.isFinite(value) || value < minimum ||
    value > maximum
  ) {
    throw new RequestError(`invalid_${key}`, 400);
  }
  return value;
}

function requiredString(
  body: JsonObject,
  key: string,
  maxLength: number,
): string {
  const value = body[key];
  if (
    typeof value !== "string" || !value.trim() ||
    value.trim().length > maxLength
  ) {
    throw new RequestError(`invalid_${key}`, 400);
  }
  return value.trim();
}

function queryInteger(
  raw: string | null,
  fallback: number,
  minimum: number,
  maximum: number,
): number {
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
  if (!match || !match[1].trim()) {
    throw new RequestError("authorization_required", 401);
  }
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
  return /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i
    .test(value);
}

function encodeCursor(recordedAt: string, id: string): string {
  return btoa(`${recordedAt}|${id}`).replaceAll("+", "-").replaceAll("/", "_")
    .replace(/=+$/, "");
}

function decodeCursor(
  raw: string | null,
): { recordedAt: string; id: string } | null {
  if (!raw) return null;
  if (!/^[A-Za-z0-9_-]{1,256}$/.test(raw)) {
    throw new RequestError("invalid_cursor", 400);
  }
  try {
    const base64 = raw.replaceAll("-", "+").replaceAll("_", "/").padEnd(
      Math.ceil(raw.length / 4) * 4,
      "=",
    );
    const [recordedAt, id, extra] = atob(base64).split("|");
    if (
      extra !== undefined || !recordedAt ||
      !Number.isFinite(Date.parse(recordedAt)) || !isUuid(id)
    ) {
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
  return configuredKey(
    "DFBLACKBOX_SUPABASE_SECRET_KEY",
    "SUPABASE_SECRET_KEYS",
    "sb_secret_",
  );
}

function getRoute(rawUrl: string): string {
  const pathname = new URL(rawUrl).pathname;
  const marker = "/recording-media";
  const markerIndex = pathname.indexOf(marker);
  return markerIndex < 0
    ? ""
    : pathname.slice(markerIndex + marker.length).replace(/^\/+|\/+$/g, "");
}

function configuredKey(
  directName: string,
  dictionaryName: string,
  prefix: string,
): string {
  const direct = Deno.env.get(directName)?.trim();
  if (direct?.startsWith(prefix)) return direct;
  const keys = configuredKeys(dictionaryName, prefix);
  if (keys.length === 0) {
    throw new RequestError("server_key_not_configured", 503);
  }
  return keys[0];
}

function isConfiguredKey(
  candidate: string,
  dictionaryName: string,
  prefix: string,
): boolean {
  return candidate.startsWith(prefix) &&
    configuredKeys(dictionaryName, prefix).includes(candidate);
}

function configuredKeys(name: string, prefix: string): string[] {
  const raw = Deno.env.get(name);
  if (!raw) return [];
  try {
    return collectStrings(JSON.parse(raw)).filter((value) =>
      value.startsWith(prefix)
    );
  } catch {
    return [];
  }
}

function collectStrings(value: unknown): string[] {
  if (typeof value === "string") return [value];
  if (Array.isArray(value)) return value.flatMap(collectStrings);
  if (value && typeof value === "object") {
    return Object.values(value).flatMap(collectStrings);
  }
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
