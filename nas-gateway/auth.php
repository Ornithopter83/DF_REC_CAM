<?php
declare(strict_types=1);

require_once __DIR__ . '/common.php';
dfbb_security_headers();

$returnUrl = dfbb_valid_return_url($_POST['return_to'] ?? null);
if (($_SERVER['REQUEST_METHOD'] ?? '') !== 'POST') {
    dfbb_redirect_to_portal($returnUrl, 'error');
}

$verified = dfbb_verify_assertion((string) ($_POST['assertion'] ?? ''));
if ($verified === false || !dfbb_start_session()) {
    dfbb_redirect_to_portal($returnUrl, 'error');
}

session_regenerate_id(true);
$_SESSION['dfblackbox'] = $verified;
session_write_close();
dfbb_redirect_to_portal($returnUrl, 'ready');
