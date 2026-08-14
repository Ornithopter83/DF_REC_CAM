<?php
declare(strict_types=1);

require_once __DIR__ . '/common.php';
dfbb_security_headers();
$returnUrl = dfbb_valid_return_url($_REQUEST['return_to'] ?? null);

if (dfbb_start_session()) {
    $_SESSION = [];
    if (ini_get('session.use_cookies')) {
        setcookie(DFBB_SESSION_NAME, '', [
            'expires' => time() - 3600,
            'path' => DFBB_SESSION_COOKIE_PATH,
            'secure' => true,
            'httponly' => true,
            'samesite' => 'Lax',
        ]);
    }
    session_destroy();
}
dfbb_redirect_to_portal($returnUrl, 'logged_out');
