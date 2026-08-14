<?php
declare(strict_types=1);

require_once __DIR__ . '/config.php';

function dfbb_security_headers(): void
{
    header('Cache-Control: no-store');
    header('Pragma: no-cache');
    header('X-Content-Type-Options: nosniff');
    header('X-Frame-Options: DENY');
    header("Content-Security-Policy: default-src 'none'; frame-ancestors 'none'; base-uri 'none'");
    header('Referrer-Policy: no-referrer');
}
function dfbb_start_session(): bool
{
    if (session_status() === PHP_SESSION_ACTIVE) {
        return true;
    }
    session_name(DFBB_SESSION_NAME);
    session_set_cookie_params([
        'lifetime' => 0,
        'path' => DFBB_SESSION_COOKIE_PATH,
        'secure' => true,
        'httponly' => true,
        'samesite' => 'Lax',
    ]);
    return session_start();
}

function dfbb_base64url_decode(string $value)
{
    if ($value === '' || preg_match('/^[A-Za-z0-9_-]+$/', $value) !== 1) {
        return false;
    }
    $padding = strlen($value) % 4;
    if ($padding !== 0) {
        $value .= str_repeat('=', 4 - $padding);
    }
    return base64_decode(strtr($value, '-_', '+/'), true);
}

function dfbb_is_uuid($value): bool
{
    return is_string($value)
        && preg_match('/^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i', $value) === 1;
}

function dfbb_safe_prefix($value)
{
    if (!is_string($value)) {
        return false;
    }
    $prefix = trim($value, '/');
    if ($prefix === '' || strlen($prefix) > 768 || strpos($prefix, '\\') !== false
        || strpos($prefix, ':') !== false || strpos($prefix, '//') !== false) {
        return false;
    }
    foreach (explode('/', $prefix) as $segment) {
        if ($segment === '' || $segment === '.' || $segment === '..') {
            return false;
        }
    }
    return $prefix;
}

function dfbb_verified_assertion_payload(string $assertion)
{
    if ($assertion === '' || strlen($assertion) > 16384) {
        return false;
    }
    $parts = explode('.', $assertion);
    if (count($parts) !== 3) {
        return false;
    }
    $headerRaw = dfbb_base64url_decode($parts[0]);
    $payloadRaw = dfbb_base64url_decode($parts[1]);
    $signature = dfbb_base64url_decode($parts[2]);
    if ($headerRaw === false || $payloadRaw === false || $signature === false) {
        return false;
    }
    $header = json_decode($headerRaw, true);
    $payload = json_decode($payloadRaw, true);
    if (!is_array($header) || !is_array($payload)
        || ($header['alg'] ?? '') !== 'RS256' || ($header['typ'] ?? '') !== 'JWT') {
        return false;
    }
    $publicKey = openssl_pkey_get_public(DFBB_ASSERTION_PUBLIC_KEY);
    if ($publicKey === false) {
        return false;
    }
    $verified = openssl_verify(
        $parts[0] . '.' . $parts[1],
        $signature,
        $publicKey,
        OPENSSL_ALGO_SHA256
    );
    openssl_free_key($publicKey);
    if ($verified !== 1) {
        return false;
    }

    return $payload;
}

function dfbb_verify_assertion(string $assertion)
{
    $payload = dfbb_verified_assertion_payload($assertion);
    if ($payload === false) {
        return false;
    }

    $now = time();
    $issuedAt = $payload['iat'] ?? null;
    $expiresAt = $payload['exp'] ?? null;
    $sessionExpiresAt = $payload['session_exp'] ?? null;
    if (($payload['v'] ?? null) !== 1
        || ($payload['iss'] ?? '') !== DFBB_ASSERTION_ISSUER
        || ($payload['aud'] ?? '') !== DFBB_ASSERTION_AUDIENCE
        || ($payload['gateway_base_url'] ?? '') !== DFBB_GATEWAY_BASE_URL
        || !dfbb_is_uuid($payload['sub'] ?? null)
        || !is_int($issuedAt) || !is_int($expiresAt) || !is_int($sessionExpiresAt)
        || $issuedAt > $now + 60 || $expiresAt < $now || $expiresAt > $issuedAt + 300
        || $sessionExpiresAt <= $now || $sessionExpiresAt > $now + DFBB_SESSION_MAX_SECONDS
        || !dfbb_is_uuid($payload['jti'] ?? null)
        || !isset($payload['scopes']) || !is_array($payload['scopes'])) {
        return false;
    }

    $scopes = [];
    foreach ($payload['scopes'] as $scope) {
        if (!is_array($scope) || !dfbb_is_uuid($scope['location_id'] ?? null)
            || !isset($scope['prefixes']) || !is_array($scope['prefixes'])) {
            return false;
        }
        $prefixes = [];
        foreach ($scope['prefixes'] as $prefixValue) {
            $prefix = dfbb_safe_prefix($prefixValue);
            if ($prefix === false) {
                return false;
            }
            $prefixes[$prefix] = true;
        }
        if (count($prefixes) === 0) {
            return false;
        }
        $scopes[strtolower($scope['location_id'])] = array_keys($prefixes);
    }
    if (count($scopes) === 0) {
        return false;
    }
    return [
        'sub' => strtolower($payload['sub']),
        'expires_at' => $sessionExpiresAt,
        'scopes' => $scopes,
    ];
}

function dfbb_authorization_bearer()
{
    $authorization = $_SERVER['HTTP_AUTHORIZATION'] ?? '';
    if (!is_string($authorization) || $authorization === '') {
        if (function_exists('getallheaders')) {
            $headers = getallheaders();
            if (is_array($headers)) {
                foreach ($headers as $name => $value) {
                    if (strcasecmp((string) $name, 'Authorization') === 0) {
                        $authorization = (string) $value;
                        break;
                    }
                }
            }
        }
    }
    if (preg_match('/^Bearer[[:space:]]+(.+)$/i', trim((string) $authorization), $matches) !== 1) {
        return false;
    }
    $token = trim($matches[1]);
    return $token !== '' && strlen($token) <= 16384 ? $token : false;
}

function dfbb_verify_upload_assertion()
{
    $assertion = dfbb_authorization_bearer();
    if ($assertion === false) {
        return false;
    }
    $payload = dfbb_verified_assertion_payload($assertion);
    if ($payload === false) {
        return false;
    }
    $now = time();
    $issuedAt = $payload['iat'] ?? null;
    $expiresAt = $payload['exp'] ?? null;
    $prefix = dfbb_safe_prefix($payload['prefix'] ?? null);
    if (($payload['v'] ?? null) !== 1
        || ($payload['iss'] ?? '') !== DFBB_ASSERTION_ISSUER
        || ($payload['aud'] ?? '') !== DFBB_UPLOAD_ASSERTION_AUDIENCE
        || ($payload['gateway_base_url'] ?? '') !== DFBB_GATEWAY_BASE_URL
        || !dfbb_is_uuid($payload['sub'] ?? null)
        || !dfbb_is_uuid($payload['camera_id'] ?? null)
        || !dfbb_is_uuid($payload['location_id'] ?? null)
        || !dfbb_is_uuid($payload['jti'] ?? null)
        || !is_int($issuedAt) || !is_int($expiresAt)
        || $issuedAt > $now + 60 || $expiresAt <= $now
        || $expiresAt > $issuedAt + DFBB_UPLOAD_SESSION_MAX_SECONDS
        || $prefix === false) {
        return false;
    }
    return [
        'device_id' => strtolower($payload['sub']),
        'camera_id' => strtolower($payload['camera_id']),
        'location_id' => strtolower($payload['location_id']),
        'prefix' => $prefix,
        'expires_at' => $expiresAt,
    ];
}

function dfbb_valid_return_url($value): string
{
    return is_string($value) && $value === DFBB_PORTAL_RETURN_URL
        ? $value
        : DFBB_PORTAL_RETURN_URL;
}

function dfbb_redirect_to_portal(string $returnUrl, string $state): void
{
    $separator = strpos($returnUrl, '?') === false ? '?' : '&';
    header('Location: ' . $returnUrl . $separator . 'nas_sso=' . rawurlencode($state), true, 303);
    exit;
}

function dfbb_authenticated_session()
{
    if (!dfbb_start_session()) {
        return false;
    }
    $session = $_SESSION['dfblackbox'] ?? null;
    if (!is_array($session) || !isset($session['expires_at'], $session['scopes'])
        || !is_int($session['expires_at']) || $session['expires_at'] <= time()
        || !is_array($session['scopes'])) {
        unset($_SESSION['dfblackbox']);
        return false;
    }
    return $session;
}

function dfbb_authorized_file(array $session, string $locationId, string $relativePath)
{
    if (!dfbb_is_uuid($locationId) || $relativePath === '' || strlen($relativePath) > 1024
        || $relativePath[0] === '/' || strpos($relativePath, '\\') !== false
        || strpos($relativePath, ':') !== false || strpos($relativePath, '//') !== false
        || strtolower(substr($relativePath, -4)) !== '.mp4') {
        return false;
    }
    foreach (explode('/', $relativePath) as $segment) {
        if ($segment === '' || $segment === '.' || $segment === '..') {
            return false;
        }
    }
    $prefixes = $session['scopes'][strtolower($locationId)] ?? null;
    if (!is_array($prefixes)) {
        return false;
    }
    $authorized = false;
    foreach ($prefixes as $prefix) {
        if (strpos($relativePath, $prefix . '/recordings/') === 0) {
            $authorized = true;
            break;
        }
    }
    if (!$authorized) {
        return false;
    }
    $root = realpath(DFBB_MEDIA_ROOT);
    $file = realpath(DFBB_MEDIA_ROOT . '/' . $relativePath);
    if ($root === false || $file === false || strpos($file, $root . DIRECTORY_SEPARATOR) !== 0
        || !is_file($file) || !is_readable($file)) {
        return false;
    }
    return $file;
}

function dfbb_fail(int $status, string $message): void
{
    http_response_code($status);
    header('Content-Type: text/plain; charset=utf-8');
    echo $message;
    exit;
}
