import {
  AccessToken,
  IngressClient,
  IngressInput,
} from "npm:livekit-server-sdk@2.17.0";

const jsonHeaders = {
  "content-type": "application/json; charset=utf-8",
};

const ingressReadyWaitAttempts = 20;
const ingressReadyWaitMilliseconds = 250;

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
    const viewerSession = route.match(
      /^cameras\/([0-9a-f-]{36})\/stream-session$/i,
    );
    if (request.method === "POST" && viewerSession) {
      return await createViewerSession(
        viewerSession[1],
        request,
        publishableKey,
      );
    }

    const viewerHeartbeat = route.match(
      /^cameras\/([0-9a-f-]{36})\/stream-heartbeat$/i,
    );
    if (request.method === "POST" && viewerHeartbeat) {
      return await renewViewerLease(
        viewerHeartbeat[1],
        request,
        publishableKey,
      );
    }

    const deviceCommand = route.match(
      /^devices\/([0-9a-f-]{36})\/stream-command$/i,
    );
    if (request.method === "GET" && deviceCommand) {
      return await getDeviceCommand(deviceCommand[1], request);
    }

    const deviceState = route.match(
      /^devices\/([0-9a-f-]{36})\/stream-state$/i,
    );
    if (request.method === "POST" && deviceState) {
      return await reportDeviceState(deviceState[1], request);
    }

    return json({ error_code: "route_not_found" }, 404, request);
  } catch (error) {
    if (error instanceof RequestError) {
      return json({ error_code: error.code }, error.status, request);
    }

    return json({ error_code: "internal_error" }, 500, request);
  }
});

async function createViewerSession(
  cameraId: string,
  request: Request,
  publishableKey: string,
): Promise<Response> {
  const userJwt = bearerToken(request);
  const requestRows = await userRpc(
    "request_camera_stream",
    { p_camera_id: cameraId },
    publishableKey,
    userJwt,
  );
  const stream = firstRow(requestRows);
  await ensureIngress(cameraId, String(stream.room_name));

  const { url, apiKey, apiSecret } = liveKitConfiguration();
  const identity = `viewer-${stream.viewer_identity}-${crypto.randomUUID()}`;
  const accessToken = new AccessToken(apiKey, apiSecret, {
    identity,
    ttl: "5m",
    metadata: JSON.stringify({ camera_id: cameraId }),
  });
  accessToken.addGrant({
    roomJoin: true,
    room: String(stream.room_name),
    canSubscribe: true,
    canPublish: false,
    canPublishData: false,
  });

  return json(
    {
      livekit_url: url,
      room_name: stream.room_name,
      participant_token: await accessToken.toJwt(),
      lease_until: stream.lease_until,
    },
    200,
    request,
  );
}

async function renewViewerLease(
  cameraId: string,
  request: Request,
  publishableKey: string,
): Promise<Response> {
  const userJwt = bearerToken(request);
  const rows = await userRpc(
    "request_camera_stream",
    { p_camera_id: cameraId },
    publishableKey,
    userJwt,
  );
  const stream = firstRow(rows);
  return json({ lease_until: stream.lease_until }, 200, request);
}

async function getDeviceCommand(
  deviceId: string,
  request: Request,
): Promise<Response> {
  const deviceToken = bearerToken(request);
  const rows = await adminRpc("get_device_stream_command", {
    p_device_id: deviceId,
    p_device_token: deviceToken,
    p_public_ip: clientPublicIp(request),
  });
  if (!Array.isArray(rows) || rows.length === 0) {
    return json({ should_stream: false }, 200, request);
  }
  const stream = firstRow(rows);
  return json(
    {
      camera_id: stream.camera_id,
      room_name: stream.room_name,
      should_stream: Boolean(stream.should_stream),
      ingress_url: stream.should_stream ? stream.ingress_url : null,
      ingress_stream_key: stream.should_stream
        ? stream.ingress_stream_key
        : null,
      lease_until: stream.lease_until,
    },
    200,
    request,
  );
}

function clientPublicIp(request: Request): string | null {
  const candidate = (request.headers.get("x-forwarded-for") ?? "")
    .split(",")[0]
    .trim();
  return /^[0-9a-f:.]{3,64}$/i.test(candidate) ? candidate : null;
}

async function reportDeviceState(
  deviceId: string,
  request: Request,
): Promise<Response> {
  const deviceToken = bearerToken(request);
  const body = await readJson(request);
  await adminRpc("report_camera_stream_state", {
    p_device_id: deviceId,
    p_device_token: deviceToken,
    p_camera_id: requiredUuid(body, "camera_id"),
    p_state: requiredEnum(body, "state", ["idle", "publishing", "error"]),
    p_error_code: optionalString(body, "error_code", 80),
  });
  return json({ accepted: true }, 200, request);
}

async function ensureIngress(
  cameraId: string,
  roomName: string,
): Promise<void> {
  let configuration = firstRow(
    await adminRpc("get_camera_stream_configuration", {
      p_camera_id: cameraId,
    }),
  );
  if (configuration.ingress_id) return;

  const claimRows = await adminRpc("begin_camera_stream_ingress", {
    p_camera_id: cameraId,
  });
  if (!singleBoolean(claimRows)) {
    configuration = await waitForIngress(cameraId);
    if (configuration?.ingress_id) return;
    if (configuration?.state === "error") {
      throw new RequestError("ingress_creation_failed", 502);
    }
    throw new RequestError("ingress_preparing", 409);
  }

  let client: IngressClient | null = null;
  let createdIngressId: string | null = null;
  try {
    const { httpUrl, apiKey, apiSecret } = liveKitConfiguration();
    client = new IngressClient(httpUrl, apiKey, apiSecret);
    const ingress = await client.createIngress(IngressInput.RTMP_INPUT, {
      name: `DFBlackbox ${cameraId}`,
      roomName,
      participantIdentity: `camera-${cameraId}`,
      participantName: "DFBlackbox Camera",
    });
    if (!ingress.ingressId || !ingress.url || !ingress.streamKey) {
      throw new Error("invalid_ingress_response");
    }
    createdIngressId = ingress.ingressId;

    await adminRpc("complete_camera_stream_ingress", {
      p_camera_id: cameraId,
      p_ingress_id: ingress.ingressId,
      p_ingress_url: ingress.url,
      p_ingress_stream_key: ingress.streamKey,
    });
    configuration = firstRow(
      await adminRpc("get_camera_stream_configuration", {
        p_camera_id: cameraId,
      }),
    );
    if (!configuration.ingress_id) throw new Error("ingress_not_persisted");
  } catch {
    if (createdIngressId) {
      const persisted = await adminRpc("get_camera_stream_configuration", {
        p_camera_id: cameraId,
      }).then((value) =>
        String(firstRow(value).ingress_id ?? "") === createdIngressId
      )
        .catch(() => false);
      if (persisted) return;
      if (client) {
        await client.deleteIngress(createdIngressId).catch(() => undefined);
      }
    }
    await adminRpc("fail_camera_stream_ingress", {
      p_camera_id: cameraId,
      p_error_code: "ingress_creation_failed",
    }).catch(() => undefined);
    throw new RequestError("ingress_creation_failed", 502);
  }
}

async function waitForIngress(cameraId: string): Promise<JsonObject | null> {
  for (let attempt = 0; attempt < ingressReadyWaitAttempts; attempt += 1) {
    await delay(ingressReadyWaitMilliseconds);
    const configuration = firstRow(
      await adminRpc("get_camera_stream_configuration", {
        p_camera_id: cameraId,
      }),
    );
    if (configuration.ingress_id || configuration.state === "error") {
      return configuration;
    }
    if (!configuration.ingress_creation_started_at) {
      return configuration;
    }
  }
  return null;
}

function delay(milliseconds: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}

function liveKitConfiguration(): {
  url: string;
  httpUrl: string;
  apiKey: string;
  apiSecret: string;
} {
  const url = requiredEnvironment("LIVEKIT_URL");
  const parsed = new URL(url);
  if (parsed.protocol !== "wss:") {
    throw new RequestError("livekit_url_not_configured", 503);
  }
  parsed.protocol = "https:";
  return {
    url,
    httpUrl: parsed.toString().replace(/\/$/, ""),
    apiKey: requiredEnvironment("LIVEKIT_API_KEY"),
    apiSecret: requiredEnvironment("LIVEKIT_API_SECRET"),
  };
}

async function adminRpc(name: string, body: JsonObject): Promise<unknown> {
  const secretKey = configuredKey(
    "DFBLACKBOX_SUPABASE_SECRET_KEY",
    "SUPABASE_SECRET_KEYS",
    "sb_secret_",
  );
  return await rpc(name, body, {
    apikey: secretKey,
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
  const supabaseUrl = Deno.env.get("SUPABASE_URL")?.trim();
  if (!supabaseUrl) throw new RequestError("supabase_url_not_configured", 503);
  const response = await fetch(`${supabaseUrl}/rest/v1/rpc/${name}`, {
    method: "POST",
    headers: { ...jsonHeaders, ...headers },
    body: JSON.stringify(body),
  });
  if (!response.ok) {
    const databaseError = await readDatabaseError(response);
    if (databaseError === "device_authentication_failed") {
      throw new RequestError(databaseError, 401);
    }
    if (databaseError === "camera_not_found") {
      throw new RequestError(databaseError, 404);
    }
    if (databaseError === "stream_not_found") {
      throw new RequestError(databaseError, 404);
    }
    if (
      databaseError.endsWith("_denied") ||
      databaseError === "authentication_required" ||
      databaseError === "operation_not_authorized"
    ) {
      throw new RequestError("operation_not_authorized", 403);
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
    const value = await response.json();
    const message =
      value && typeof value === "object" && !Array.isArray(value) &&
        typeof (value as JsonObject).message === "string"
        ? String((value as JsonObject).message).trim()
        : "";
    return message.match(/^[a-z][a-z0-9_]{1,79}$/)?.[0] ?? "";
  } catch {
    return "";
  }
}

async function readJson(request: Request): Promise<JsonObject> {
  try {
    const raw = await request.text();
    if (new TextEncoder().encode(raw).byteLength > 4096) {
      throw new RequestError("request_too_large", 413);
    }
    const value = JSON.parse(raw);
    if (!value || typeof value !== "object" || Array.isArray(value)) {
      throw new Error();
    }
    return value as JsonObject;
  } catch (error) {
    if (error instanceof RequestError) throw error;
    throw new RequestError("invalid_json", 400);
  }
}

function requiredUuid(body: JsonObject, key: string): string {
  const value = requiredString(body, key, 36);
  if (
    !/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i
      .test(value)
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

function requiredEnum(body: JsonObject, key: string, values: string[]): string {
  const value = requiredString(body, key, 32).toLowerCase();
  if (!values.includes(value)) throw new RequestError(`invalid_${key}`, 400);
  return value;
}

function optionalString(
  body: JsonObject,
  key: string,
  maxLength: number,
): string | null {
  const value = body[key];
  if (value === null || value === undefined || value === "") return null;
  if (typeof value !== "string" || value.trim().length > maxLength) {
    throw new RequestError(`invalid_${key}`, 400);
  }
  return value.trim();
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
  if (
    !Array.isArray(value) || value.length !== 1 || !value[0] ||
    typeof value[0] !== "object"
  ) {
    throw new RequestError("invalid_database_response", 502);
  }
  return value[0] as JsonObject;
}

function singleBoolean(value: unknown): boolean {
  return typeof value === "boolean"
    ? value
    : Array.isArray(value) && value.length === 1 &&
        typeof value[0] === "boolean"
    ? value[0]
    : false;
}

function requiredEnvironment(name: string): string {
  const value = Deno.env.get(name)?.trim();
  if (!value) throw new RequestError("livekit_not_configured", 503);
  return value;
}

function getRoute(rawUrl: string): string {
  const pathname = new URL(rawUrl).pathname;
  const marker = "/media-session";
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
