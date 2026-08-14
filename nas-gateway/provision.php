<?php
declare(strict_types=1);

require_once __DIR__ . '/common.php';
dfbb_security_headers();

$scope = dfbb_verify_upload_assertion();
if ($scope === false) {
    dfbb_provision_fail(401, 'upload_session_required');
}

$method = $_SERVER['REQUEST_METHOD'] ?? '';
if ($method !== 'POST') {
    header('Allow: POST');
    dfbb_provision_fail(405, 'method_not_allowed');
}

$root = realpath(DFBB_MEDIA_ROOT);
if ($root === false || !is_dir($root) || !is_writable($root)) {
    dfbb_provision_fail(503, 'nas_media_root_unavailable');
}

$cameraRoot = dfbb_provision_directory($root, $scope['prefix']);
foreach (['live', 'recordings', 'events', 'temp'] as $folder) {
    dfbb_provision_directory($cameraRoot, $folder);
}

$testPath = $cameraRoot . '/.dfblackbox-write-test-' . bin2hex(random_bytes(8)) . '.tmp';
$test = fopen($testPath, 'x+b');
if ($test === false) {
    dfbb_provision_fail(503, 'nas_write_test_failed');
}
try {
    $written = fwrite($test, 'DFBlackbox');
    $flushed = fflush($test);
} finally {
    fclose($test);
    @unlink($testPath);
}
if ($written !== 10 || !$flushed) {
    dfbb_provision_fail(503, 'nas_write_test_failed');
}

http_response_code(200);
header('Content-Type: application/json; charset=utf-8');
echo json_encode([
    'state' => 'ready',
    'nas_relative_path' => $scope['prefix'],
], JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE);
exit;

function dfbb_provision_directory(string $root, string $relativePath): string
{
    $current = realpath($root);
    if ($current === false) {
        dfbb_provision_fail(503, 'nas_media_root_unavailable');
    }
    foreach (explode('/', $relativePath) as $segment) {
        if ($segment === '' || $segment === '.' || $segment === '..') {
            dfbb_provision_fail(400, 'invalid_nas_relative_path');
        }
        $candidate = $current . DIRECTORY_SEPARATOR . $segment;
        if (is_link($candidate)) {
            dfbb_provision_fail(409, 'nas_path_symlink_denied');
        }
        if (!is_dir($candidate) && !mkdir($candidate, 0750) && !is_dir($candidate)) {
            dfbb_provision_fail(503, 'nas_directory_creation_failed');
        }
        $resolved = realpath($candidate);
        if ($resolved === false
            || strpos($resolved, $current . DIRECTORY_SEPARATOR) !== 0
            || dirname($resolved) !== $current) {
            dfbb_provision_fail(409, 'invalid_nas_directory');
        }
        $current = $resolved;
    }
    return $current;
}

function dfbb_provision_fail(int $status, string $errorCode): void
{
    http_response_code($status);
    header('Content-Type: application/json; charset=utf-8');
    echo json_encode(['error_code' => $errorCode], JSON_UNESCAPED_SLASHES);
    exit;
}
