<?php
declare(strict_types=1);

require_once __DIR__ . '/common.php';
dfbb_security_headers();

$scope = dfbb_verify_upload_assertion();
if ($scope === false) {
    dfbb_upload_fail(401, 'upload_session_required');
}

$method = $_SERVER['REQUEST_METHOD'] ?? '';
$uploadId = strtolower(trim((string) ($_GET['upload'] ?? '')));
if ($method === 'POST' && $uploadId === '') {
    dfbb_create_upload($scope);
}
if (preg_match('/^[0-9a-f]{64}$/', $uploadId) !== 1) {
    dfbb_upload_fail(400, 'invalid_upload_id');
}
if ($method === 'HEAD') {
    dfbb_upload_status($scope, $uploadId);
}
if ($method === 'PATCH') {
    dfbb_append_upload($scope, $uploadId);
}
if ($method === 'POST') {
    dfbb_complete_upload($scope, $uploadId);
}

header('Allow: POST, HEAD, PATCH');
dfbb_upload_fail(405, 'method_not_allowed');

function dfbb_create_upload(array $scope): void
{
    $body = dfbb_upload_json_body();
    $relativePath = dfbb_upload_relative_path($body['relative_path'] ?? null);
    $fileSize = $body['file_size_bytes'] ?? null;
    $fingerprint = strtolower(trim((string) ($body['source_fingerprint'] ?? '')));
    if (!is_int($fileSize) || $fileSize < 1 || $fileSize > DFBB_UPLOAD_MAX_FILE_BYTES) {
        dfbb_upload_fail(400, 'invalid_file_size');
    }
    if (preg_match('/^[0-9a-f]{64}$/', $fingerprint) !== 1) {
        dfbb_upload_fail(400, 'invalid_source_fingerprint');
    }
    $lastModifiedUnix = null;
    if (isset($body['source_last_modified_at']) && $body['source_last_modified_at'] !== null
        && $body['source_last_modified_at'] !== '') {
        if (!is_string($body['source_last_modified_at'])) {
            dfbb_upload_fail(400, 'invalid_source_last_modified_at');
        }
        $lastModifiedUnix = strtotime($body['source_last_modified_at']);
        if ($lastModifiedUnix === false || $lastModifiedUnix > time() + 300) {
            dfbb_upload_fail(400, 'invalid_source_last_modified_at');
        }
    }

    $uploadId = hash('sha256', implode('|', [
        $scope['device_id'],
        $scope['camera_id'],
        $scope['location_id'],
        $scope['prefix'],
        $relativePath,
        $fingerprint,
    ]));
    $paths = dfbb_upload_paths($scope, $uploadId);
    $lock = dfbb_upload_lock($paths['lock']);
    try {
        $finalPath = dfbb_upload_final_path($paths['camera_root'], $relativePath, false);
        if ($finalPath !== false && is_file($finalPath)) {
            if (filesize($finalPath) === $fileSize
                && hash_equals($fingerprint, (string) hash_file('sha256', $finalPath))) {
                dfbb_upload_headers($uploadId, $fileSize, $fileSize, 'complete');
                dfbb_upload_json([
                    'upload_id' => $uploadId,
                    'offset' => $fileSize,
                    'length' => $fileSize,
                    'state' => 'complete',
                ]);
            }
            dfbb_upload_fail(409, 'nas_file_conflict');
        }

        $expected = [
            'version' => 1,
            'device_id' => $scope['device_id'],
            'camera_id' => $scope['camera_id'],
            'location_id' => $scope['location_id'],
            'prefix' => $scope['prefix'],
            'relative_path' => $relativePath,
            'file_size_bytes' => $fileSize,
            'source_fingerprint' => $fingerprint,
            'source_last_modified_unix' => $lastModifiedUnix,
        ];
        if (is_file($paths['state'])) {
            $state = dfbb_upload_state($scope, $paths['state']);
            foreach ($expected as $key => $value) {
                if (($state[$key] ?? null) !== $value) {
                    dfbb_upload_fail(409, 'upload_metadata_conflict');
                }
            }
        } else {
            $state = $expected;
            $state['created_at'] = time();
            dfbb_write_upload_state($paths['state'], $state);
        }
        if (!is_file($paths['temporary'])) {
            $handle = fopen($paths['temporary'], 'x+b');
            if ($handle === false) {
                dfbb_upload_fail(500, 'upload_file_creation_failed');
            }
            fclose($handle);
        }
        $offset = filesize($paths['temporary']);
        if ($offset === false || $offset < 0 || $offset > $fileSize) {
            dfbb_upload_fail(409, 'invalid_upload_offset');
        }
        dfbb_upload_headers($uploadId, $offset, $fileSize, 'uploading');
        dfbb_upload_json([
            'upload_id' => $uploadId,
            'offset' => $offset,
            'length' => $fileSize,
            'state' => 'uploading',
        ], 201);
    } finally {
        dfbb_upload_unlock($lock);
    }
}

function dfbb_upload_status(array $scope, string $uploadId): void
{
    $paths = dfbb_upload_paths($scope, $uploadId);
    $lock = dfbb_upload_lock($paths['lock']);
    try {
        $state = dfbb_upload_state($scope, $paths['state']);
        $offset = is_file($paths['temporary']) ? filesize($paths['temporary']) : false;
        $length = $state['file_size_bytes'];
        if ($offset === false || $offset < 0 || $offset > $length) {
            dfbb_upload_fail(409, 'invalid_upload_offset');
        }
        dfbb_upload_headers($uploadId, $offset, $length, 'uploading');
        http_response_code(204);
        exit;
    } finally {
        dfbb_upload_unlock($lock);
    }
}

function dfbb_append_upload(array $scope, string $uploadId): void
{
    set_time_limit(300);
    $contentType = strtolower(trim((string) ($_SERVER['CONTENT_TYPE'] ?? '')));
    if ($contentType !== 'application/offset+octet-stream') {
        dfbb_upload_fail(415, 'invalid_content_type');
    }
    $contentLengthRaw = trim((string) ($_SERVER['CONTENT_LENGTH'] ?? ''));
    $offsetRaw = trim((string) ($_SERVER['HTTP_UPLOAD_OFFSET'] ?? ''));
    if (preg_match('/^[0-9]+$/', $contentLengthRaw) !== 1
        || preg_match('/^[0-9]+$/', $offsetRaw) !== 1) {
        dfbb_upload_fail(400, 'invalid_upload_headers');
    }
    $contentLength = (int) $contentLengthRaw;
    $requestedOffset = (int) $offsetRaw;
    if ($contentLength < 1 || $contentLength > DFBB_UPLOAD_CHUNK_MAX_BYTES) {
        dfbb_upload_fail(413, 'invalid_chunk_size');
    }

    $paths = dfbb_upload_paths($scope, $uploadId);
    $lock = dfbb_upload_lock($paths['lock']);
    try {
        $state = dfbb_upload_state($scope, $paths['state']);
        $length = $state['file_size_bytes'];
        $currentOffset = is_file($paths['temporary']) ? filesize($paths['temporary']) : false;
        if ($currentOffset === false || $currentOffset !== $requestedOffset) {
            if (is_int($currentOffset)) {
                header('Upload-Offset: ' . $currentOffset);
            }
            dfbb_upload_fail(409, 'upload_offset_mismatch');
        }
        if ($currentOffset + $contentLength > $length) {
            dfbb_upload_fail(413, 'upload_exceeds_length');
        }
        $freeBytes = disk_free_space(dirname($paths['temporary']));
        if ($freeBytes !== false && $freeBytes < $contentLength + 67108864) {
            dfbb_upload_fail(507, 'nas_space_insufficient');
        }

        $input = fopen('php://input', 'rb');
        $target = fopen($paths['temporary'], 'c+b');
        if ($input === false || $target === false || fseek($target, $currentOffset) !== 0) {
            if (is_resource($input)) {
                fclose($input);
            }
            if (is_resource($target)) {
                fclose($target);
            }
            dfbb_upload_fail(500, 'upload_stream_failed');
        }
        $remaining = $contentLength;
        while ($remaining > 0) {
            $chunk = fread($input, min(1048576, $remaining));
            if ($chunk === false || $chunk === '') {
                break;
            }
            $written = fwrite($target, $chunk);
            if ($written === false || $written !== strlen($chunk)) {
                break;
            }
            $remaining -= $written;
        }
        fflush($target);
        fclose($target);
        fclose($input);
        clearstatcache(true, $paths['temporary']);
        $newOffset = filesize($paths['temporary']);
        if ($remaining !== 0 || $newOffset === false || $newOffset !== $currentOffset + $contentLength) {
            dfbb_upload_fail(400, 'chunk_incomplete');
        }
        dfbb_upload_headers($uploadId, $newOffset, $length, 'uploading');
        http_response_code(204);
        exit;
    } finally {
        dfbb_upload_unlock($lock);
    }
}

function dfbb_complete_upload(array $scope, string $uploadId): void
{
    set_time_limit(0);
    $paths = dfbb_upload_paths($scope, $uploadId);
    $lock = dfbb_upload_lock($paths['lock']);
    try {
        $state = dfbb_upload_state($scope, $paths['state']);
        $length = $state['file_size_bytes'];
        $offset = is_file($paths['temporary']) ? filesize($paths['temporary']) : false;
        if ($offset === false || $offset !== $length) {
            if (is_int($offset)) {
                header('Upload-Offset: ' . $offset);
            }
            dfbb_upload_fail(409, 'upload_incomplete');
        }
        $actualFingerprint = hash_file('sha256', $paths['temporary']);
        if (!is_string($actualFingerprint)
            || !hash_equals($state['source_fingerprint'], strtolower($actualFingerprint))) {
            $handle = fopen($paths['temporary'], 'c+b');
            if ($handle !== false) {
                ftruncate($handle, 0);
                fclose($handle);
            }
            header('Upload-Offset: 0');
            dfbb_upload_fail(422, 'source_fingerprint_mismatch');
        }

        $targetLockPath = dirname($paths['state']) . '/target-'
            . hash('sha256', $state['relative_path']) . '.lock';
        $targetLock = dfbb_upload_lock($targetLockPath);
        try {
            $finalPath = dfbb_upload_final_path($paths['camera_root'], $state['relative_path'], true);
            if ($finalPath === false) {
                dfbb_upload_fail(500, 'nas_target_creation_failed');
            }
            if (is_file($finalPath)) {
                if (filesize($finalPath) !== $length
                    || !hash_equals($state['source_fingerprint'], (string) hash_file('sha256', $finalPath))) {
                    dfbb_upload_fail(409, 'nas_file_conflict');
                }
                unlink($paths['temporary']);
            } elseif (!rename($paths['temporary'], $finalPath)) {
                dfbb_upload_fail(500, 'nas_finalize_failed');
            }
        } finally {
            dfbb_upload_unlock($targetLock);
        }
        if (is_int($state['source_last_modified_unix'] ?? null)) {
            touch($finalPath, $state['source_last_modified_unix']);
        }
        unlink($paths['state']);
        dfbb_upload_headers($uploadId, $length, $length, 'complete');
        dfbb_upload_json([
            'upload_id' => $uploadId,
            'offset' => $length,
            'length' => $length,
            'state' => 'complete',
            'nas_relative_path' => $scope['prefix'] . '/recordings/' . $state['relative_path'],
        ]);
    } finally {
        dfbb_upload_unlock($lock);
    }
}

function dfbb_upload_paths(array $scope, string $uploadId): array
{
    $root = realpath(DFBB_MEDIA_ROOT);
    $cameraRoot = realpath(DFBB_MEDIA_ROOT . '/' . $scope['prefix']);
    if ($root === false || $cameraRoot === false
        || strpos($cameraRoot, $root . DIRECTORY_SEPARATOR) !== 0) {
        dfbb_upload_fail(404, 'nas_camera_root_not_found');
    }
    $stateDirectory = $cameraRoot . '/' . DFBB_UPLOAD_STATE_RELATIVE_PATH;
    if (!is_dir($stateDirectory) && !mkdir($stateDirectory, 0700, true) && !is_dir($stateDirectory)) {
        dfbb_upload_fail(500, 'upload_state_creation_failed');
    }
    $stateRoot = realpath($stateDirectory);
    if ($stateRoot === false || strpos($stateRoot, $cameraRoot . DIRECTORY_SEPARATOR) !== 0) {
        dfbb_upload_fail(500, 'invalid_upload_state_path');
    }
    return [
        'camera_root' => $cameraRoot,
        'state' => $stateRoot . '/' . $uploadId . '.json',
        'temporary' => $stateRoot . '/' . $uploadId . '.uploading',
        'lock' => $stateRoot . '/' . $uploadId . '.lock',
    ];
}

function dfbb_upload_final_path(string $cameraRoot, string $relativePath, bool $createParent)
{
    $recordingsRoot = realpath($cameraRoot . '/recordings');
    if ($recordingsRoot === false || strpos($recordingsRoot, $cameraRoot . DIRECTORY_SEPARATOR) !== 0) {
        return false;
    }
    $parentRelative = dirname($relativePath);
    $parent = $parentRelative === '.'
        ? $recordingsRoot
        : $recordingsRoot . '/' . $parentRelative;
    if ($createParent && !is_dir($parent) && !mkdir($parent, 0750, true) && !is_dir($parent)) {
        return false;
    }
    $parentReal = realpath($parent);
    if ($parentReal === false
        || ($parentReal !== $recordingsRoot
            && strpos($parentReal, $recordingsRoot . DIRECTORY_SEPARATOR) !== 0)) {
        return false;
    }
    return $parentReal . '/' . basename($relativePath);
}

function dfbb_upload_relative_path($value): string
{
    if (!is_string($value)) {
        dfbb_upload_fail(400, 'invalid_relative_path');
    }
    if ($value === '' || $value[0] === '/' || substr($value, -1) === '/') {
        dfbb_upload_fail(400, 'invalid_relative_path');
    }
    $path = trim($value, '/');
    if ($path === '' || strlen($path) > 1024
        || strpos($path, '\\') !== false || strpos($path, ':') !== false
        || strpos($path, '//') !== false || strtolower(substr($path, -4)) !== '.mp4') {
        dfbb_upload_fail(400, 'invalid_relative_path');
    }
    foreach (explode('/', $path) as $segment) {
        if ($segment === '' || $segment === '.' || $segment === '..' || strlen($segment) > 255) {
            dfbb_upload_fail(400, 'invalid_relative_path');
        }
    }
    return $path;
}

function dfbb_upload_state(array $scope, string $statePath): array
{
    if (!is_file($statePath)) {
        dfbb_upload_fail(404, 'upload_not_found');
    }
    $raw = file_get_contents($statePath);
    $state = is_string($raw) ? json_decode($raw, true) : null;
    if (!is_array($state)
        || ($state['version'] ?? null) !== 1
        || ($state['device_id'] ?? null) !== $scope['device_id']
        || ($state['camera_id'] ?? null) !== $scope['camera_id']
        || ($state['location_id'] ?? null) !== $scope['location_id']
        || ($state['prefix'] ?? null) !== $scope['prefix']
        || !is_int($state['file_size_bytes'] ?? null)
        || $state['file_size_bytes'] < 1
        || $state['file_size_bytes'] > DFBB_UPLOAD_MAX_FILE_BYTES
        || preg_match('/^[0-9a-f]{64}$/', (string) ($state['source_fingerprint'] ?? '')) !== 1) {
        dfbb_upload_fail(409, 'invalid_upload_state');
    }
    $state['relative_path'] = dfbb_upload_relative_path($state['relative_path'] ?? null);
    return $state;
}

function dfbb_write_upload_state(string $statePath, array $state): void
{
    $temporary = $statePath . '.' . bin2hex(random_bytes(8)) . '.tmp';
    $json = json_encode($state, JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE);
    if (!is_string($json) || file_put_contents($temporary, $json, LOCK_EX) === false
        || !rename($temporary, $statePath)) {
        @unlink($temporary);
        dfbb_upload_fail(500, 'upload_state_write_failed');
    }
}

function dfbb_upload_lock(string $lockPath)
{
    $lock = fopen($lockPath, 'c+b');
    if ($lock === false || !flock($lock, LOCK_EX)) {
        if (is_resource($lock)) {
            fclose($lock);
        }
        dfbb_upload_fail(503, 'upload_lock_failed');
    }
    return $lock;
}

function dfbb_upload_unlock($lock): void
{
    if (is_resource($lock)) {
        flock($lock, LOCK_UN);
        fclose($lock);
    }
}

function dfbb_upload_json_body(): array
{
    $raw = file_get_contents('php://input');
    if (!is_string($raw) || strlen($raw) > 8192) {
        dfbb_upload_fail(413, 'request_too_large');
    }
    $body = json_decode($raw, true);
    if (!is_array($body)) {
        dfbb_upload_fail(400, 'invalid_json');
    }
    return $body;
}

function dfbb_upload_headers(string $uploadId, int $offset, int $length, string $state): void
{
    header('Upload-Id: ' . $uploadId);
    header('Upload-Offset: ' . $offset);
    header('Upload-Length: ' . $length);
    header('Upload-State: ' . $state);
    header('Upload-Chunk-Max: ' . DFBB_UPLOAD_CHUNK_MAX_BYTES);
}

function dfbb_upload_json(array $value, int $status = 200): void
{
    http_response_code($status);
    header('Content-Type: application/json; charset=utf-8');
    echo json_encode($value, JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE);
    exit;
}

function dfbb_upload_fail(int $status, string $code): void
{
    dfbb_upload_json(['error_code' => $code], $status);
}
