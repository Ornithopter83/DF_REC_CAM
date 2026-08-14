<?php
declare(strict_types=1);

const DFBB_GATEWAY_BASE_URL = 'https://dfblackbox-nas.duckdns.org:8443/dfblackbox/';
const DFBB_PORTAL_RETURN_URL = 'https://ornithopter83.github.io/DF_REC_CAM/';
const DFBB_MEDIA_ROOT = '/mnt/HDD1/Media';
const DFBB_SESSION_NAME = 'DFBLACKBOX_NAS_SESSION';
const DFBB_SESSION_COOKIE_PATH = '/dfblackbox/';
const DFBB_SESSION_MAX_SECONDS = 28800;
const DFBB_ASSERTION_ISSUER = 'dfblackbox-recording-media';
const DFBB_ASSERTION_AUDIENCE = 'dfblackbox-nas-gateway';

const DFBB_ASSERTION_PUBLIC_KEY = <<<'PEM'
-----BEGIN PUBLIC KEY-----
MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAuli/ZnqvsfcYbTMkoU9b
140H3WELS9x1S4t8xboCo+9T507FzZAYmeUMdhTPZ2DNwG6i8UiddD1/54w+4cC8
Z9UZb3KmXPp4T+DaDzkFPZJJ9vOqehbC2324E0xYrqYdErhK7M9cQXWuEYmx1CPo
H/Z2RxMX8a4j4ZfpVuILN6tZm7S2hLGUQcsrphA1Yo6vQ4Z9ipObhMx/eB7kPS+x
XfdsqZOOaVGICkhjD/VVNifDC+LsIciZnb7b0lQC8OxbzMjcwT7jSApKQUcO4mBF
U4vyWlo3AjsytTlvQK6KVEz4uPJurm4I5N+Dkwxo6SJgpen6UAWzfhIuPDEmrHHU
xQIDAQAB
-----END PUBLIC KEY-----
PEM;
