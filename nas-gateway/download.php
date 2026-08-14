<?php
declare(strict_types=1);

require_once __DIR__ . '/common.php';
dfbb_security_headers();

$method = $_SERVER['REQUEST_METHOD'] ?? '';
if ($method !== 'GET' && $method !== 'HEAD') {
    header('Allow: GET, HEAD');
    dfbb_fail(405, 'Method not allowed.');
}

$session = dfbb_authenticated_session();
if ($session === false) {
    dfbb_fail(401, 'NAS session is required. Return to the DFBlackbox portal and sign in again.');
}

$recordingId = (string) ($_GET['recording'] ?? '');
$locationId = (string) ($_GET['location'] ?? '');
$relativePath = (string) ($_GET['path'] ?? '');
if (!dfbb_is_uuid($recordingId)) {
    dfbb_fail(400, 'Invalid recording request.');
}
$filePath = dfbb_authorized_file($session, $locationId, $relativePath);
if ($filePath === false) {
    dfbb_fail(404, 'Recording not found.');
}

$fileSize = filesize($filePath);
if ($fileSize === false || $fileSize < 1) {
    dfbb_fail(404, 'Recording not found.');
}

$start = 0;
$end = $fileSize - 1;
$range = isset($_SERVER['HTTP_RANGE']) ? trim((string) $_SERVER['HTTP_RANGE']) : '';
if ($range !== '') {
    if (strpos($range, ',') !== false
        || preg_match('/^bytes=(\d*)-(\d*)$/', $range, $matches) !== 1
        || ($matches[1] === '' && $matches[2] === '')) {
        header('Content-Range: bytes */' . $fileSize);
        dfbb_fail(416, 'Invalid range.');
    }
    if ($matches[1] === '') {
        $suffixLength = (int) $matches[2];
        if ($suffixLength < 1) {
            header('Content-Range: bytes */' . $fileSize);
            dfbb_fail(416, 'Invalid range.');
        }
        $start = max(0, $fileSize - $suffixLength);
    } else {
        $start = (int) $matches[1];
        if ($matches[2] !== '') {
            $end = min((int) $matches[2], $end);
        }
    }
    if ($start > $end || $start >= $fileSize) {
        header('Content-Range: bytes */' . $fileSize);
        dfbb_fail(416, 'Invalid range.');
    }
    http_response_code(206);
    header('Content-Range: bytes ' . $start . '-' . $end . '/' . $fileSize);
}

$length = $end - $start + 1;
$fileName = basename($filePath);
header('Content-Type: application/octet-stream');
header('Content-Disposition: attachment; filename="recording.mp4"; filename*=UTF-8\'\'' . rawurlencode($fileName));
header('Accept-Ranges: bytes');
header('Content-Length: ' . $length);
session_write_close();

if ($method === 'HEAD') {
    exit;
}

while (ob_get_level() > 0) {
    ob_end_clean();
}
set_time_limit(0);
$handle = fopen($filePath, 'rb');
if ($handle === false || fseek($handle, $start) !== 0) {
    if (is_resource($handle)) {
        fclose($handle);
    }
    exit;
}
$remaining = $length;
while ($remaining > 0 && !connection_aborted()) {
    $chunk = fread($handle, min(1048576, $remaining));
    if ($chunk === false || $chunk === '') {
        break;
    }
    echo $chunk;
    flush();
    $remaining -= strlen($chunk);
}
fclose($handle);
