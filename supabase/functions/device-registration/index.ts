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
    if (request.method === "POST" && route === "device-claims") {
      return await createClaim(request);
    }

    const claimStatus = route.match(/^device-claims\/([0-9a-f-]{36})\/status$/i);
    if (request.method === "GET" && claimStatus) {
      return await getClaimStatus(claimStatus[1], request);
    }

    const claimApproval = route.match(/^device-claims\/([A-Z0-9-]{6,32})\/approve$/i);
    if (request.method === "POST" && claimApproval) {
      return await approveClaim(claimApproval[1], request, publishableKey);
    }

    const claimRejection = route.match(/^device-claims\/([A-Z0-9-]{6,32})\/reject$/i);
    if (request.method === "POST" && claimRejection) {
      return await rejectClaim(claimRejection[1], request, publishableKey);
    }

    const provisioning = route.match(/^devices\/([0-9a-f-]{36})\/provisioning-result$/i);
    if (request.method === "POST" && provisioning) {
      return await reportProvisioning(provisioning[1], request);
    }

    return json({ error_code: "route_not_found" }, 404, request);
  } catch (error) {
    if (error instanceof RequestError) {
      return json({ error_code: error.code }, error.status, request);
    }

    return json({ error_code: "internal_error" }, 500, request);
  }
});

async function createClaim(request: Request): Promise<Response> {
  const approvalBaseUrl = Deno.env.get("APPROVAL_BASE_URL")?.trim();
  if (!approvalBaseUrl || !isHttpsUrl(approvalBaseUrl)) {
    throw new RequestError("approval_url_not_configured", 503);
  }

  const body = await readJson(request);
  const appVersion = requiredString(body, "app_version", 64);
  const installationId = requiredString(body, "installation_id", 128);
  const cameraType = requiredString(body, "camera_type", 8).toUpperCase();
  if (cameraType !== "IP" && cameraType !== "USB") {
    throw new RequestError("invalid_camera_type", 400);
  }

  const claimCode = randomClaimCode();
  const expiresAt = new Date(Date.now() + 5 * 60 * 1000).toISOString();
  const rows = await adminRpc("create_device_claim", {
    p_claim_code: claimCode,
    p_app_version: appVersion,
    p_installation_id: installationId,
    p_camera_type: cameraType,
    p_expires_at: expiresAt,
  });
  const row = firstRow(rows);
  const approvalUrl = new URL(approvalBaseUrl);
  approvalUrl.searchParams.set("code", claimCode);

  return json({
    claim_id: row.claim_id,
    claim_code: claimCode,
    approval_url: approvalUrl.toString(),
    expires_at: row.expires_at,
  }, 201, request);
}

async function getClaimStatus(claimId: string, request: Request): Promise<Response> {
  const rows = await adminRpc("consume_device_claim", { p_claim_id: claimId });
  const row = firstRow(rows);
  return json({
    status: row.status,
    device_id: row.device_id ?? null,
    camera_id: row.camera_id ?? null,
    device_token: row.device_token ?? null,
    nas_relative_path: row.nas_relative_path ?? null,
  }, 200, request);
}

async function approveClaim(
  claimCode: string,
  request: Request,
  publishableKey: string,
): Promise<Response> {
  const userJwt = bearerToken(request);
  const body = await readJson(request);
  const organizationId = requiredUuid(body, "organization_id");
  const siteId = requiredUuid(body, "site_id");
  const deviceName = optionalString(body, "device_name", 120);
  const rows = await userRpc("approve_device_claim", {
    p_claim_code: claimCode,
    p_organization_id: organizationId,
    p_site_id: siteId,
    p_device_name: deviceName,
  }, publishableKey, userJwt);
  return json(firstRow(rows), 200, request);
}

async function rejectClaim(
  claimCode: string,
  request: Request,
  publishableKey: string,
): Promise<Response> {
  const userJwt = bearerToken(request);
  const body = await readJson(request);
  const organizationId = requiredUuid(body, "organization_id");
  await userRpc("reject_device_claim", {
    p_claim_code: claimCode,
    p_organization_id: organizationId,
  }, publishableKey, userJwt);
  return json({ rejected: true }, 200, request);
}

async function reportProvisioning(deviceId: string, request: Request): Promise<Response> {
  const deviceToken = bearerToken(request);
  const body = await readJson(request);
  const registrationState = requiredString(body, "registration_state", 32);
  const storageState = requiredString(body, "storage_state", 32);
  const errorCode = optionalString(body, "error_code", 80);
  await adminRpc("report_device_provisioning", {
    p_device_id: deviceId,
    p_device_token: deviceToken,
    p_registration_state: registrationState,
    p_storage_state: storageState,
    p_error_code: errorCode,
  });
  return json({ accepted: true }, 200, request);
}

async function adminRpc(name: string, body: JsonObject): Promise<unknown> {
  const secretKey = configuredKey("DFBLACKBOX_SUPABASE_SECRET_KEY", "SUPABASE_SECRET_KEYS", "sb_secret_");
  return await rpc(name, body, { apikey: secretKey });
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
  const supabaseUrl = Deno.env.get("SUPABASE_URL")?.trim();
  if (!supabaseUrl) throw new RequestError("supabase_url_not_configured", 503);

  const response = await fetch(`${supabaseUrl}/rest/v1/rpc/${name}`, {
    method: "POST",
    headers: { ...jsonHeaders, ...headers },
    body: JSON.stringify(body),
  });
  if (!response.ok) {
    const status = response.status === 401 || response.status === 403 ? 403 : 502;
    throw new RequestError(status === 403 ? "operation_not_authorized" : "database_operation_failed", status);
  }

  if (response.status === 204) return null;
  return await response.json();
}

async function readJson(request: Request): Promise<JsonObject> {
  const contentLength = Number(request.headers.get("content-length") ?? "0");
  if (contentLength > 4096) throw new RequestError("request_too_large", 413);
  try {
    const value = await request.json();
    if (!value || typeof value !== "object" || Array.isArray(value)) throw new Error();
    return value as JsonObject;
  } catch {
    throw new RequestError("invalid_json", 400);
  }
}

function requiredString(body: JsonObject, key: string, maxLength: number): string {
  const value = body[key];
  if (typeof value !== "string" || !value.trim() || value.trim().length > maxLength) {
    throw new RequestError(`invalid_${key}`, 400);
  }
  return value.trim();
}

function optionalString(body: JsonObject, key: string, maxLength: number): string | null {
  const value = body[key];
  if (value === null || value === undefined || value === "") return null;
  if (typeof value !== "string" || value.trim().length > maxLength) {
    throw new RequestError(`invalid_${key}`, 400);
  }
  return value.trim();
}

function requiredUuid(body: JsonObject, key: string): string {
  const value = requiredString(body, key, 36);
  if (!/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(value)) {
    throw new RequestError(`invalid_${key}`, 400);
  }
  return value;
}

function bearerToken(request: Request): string {
  const authorization = request.headers.get("authorization") ?? "";
  const match = authorization.match(/^Bearer\s+(.+)$/i);
  if (!match || !match[1].trim()) throw new RequestError("authorization_required", 401);
  return match[1].trim();
}

function randomClaimCode(): string {
  const alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
  const bytes = crypto.getRandomValues(new Uint8Array(8));
  return Array.from(bytes, (value) => alphabet[value % alphabet.length]).join("");
}

function firstRow(value: unknown): JsonObject {
  if (!Array.isArray(value) || value.length !== 1 || !value[0] || typeof value[0] !== "object") {
    throw new RequestError("invalid_database_response", 502);
  }
  return value[0] as JsonObject;
}

function getRoute(rawUrl: string): string {
  const pathname = new URL(rawUrl).pathname;
  const marker = "/device-registration";
  const markerIndex = pathname.indexOf(marker);
  return markerIndex < 0
    ? ""
    : pathname.slice(markerIndex + marker.length).replace(/^\/+|\/+$/g, "");
}

function isHttpsUrl(value: string): boolean {
  try {
    return new URL(value).protocol === "https:";
  } catch {
    return false;
  }
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
