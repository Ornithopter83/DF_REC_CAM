# DFBlackbox NAS Gateway

`provision.php`는 장치 토큰을 직접 받지 않고 Edge가 서명한 카메라 범위 assertion만 검증한다. 허용된 카메라 루트와 `live`, `recordings`, `events`, `temp` 폴더만 생성하고 쓰기 검사를 수행한다.

NAS1DUAL의 Apache/PHP 7.3 환경에서 Supabase 웹 로그인 사용자를 NAS 세션으로
교환하고, 허용된 녹화 MP4만 attachment·Range 다운로드로 제공한다. 장치는 별도
audience의 서명 세션으로 카메라 경로에 MP4를 조각·이어올리기한다.

## 배포 위치

저장소의 이 폴더 내용을 NAS `/HDD1/DocRoot/dfblackbox`에 복사한다. Apache의
실제 PHP 경로는 `/mnt/HDD1/DocRoot/dfblackbox`이고 녹화 루트는
`/mnt/HDD1/Media`다.

`config.php`에는 Edge Function의 RSA 개인키와 짝인 공개키만 둔다. 개인키,
Supabase secret key, NAS 계정 정보는 NAS 파일이나 저장소에 두지 않는다.

## 공개 경로

* `auth.php`: Edge가 서명한 로그인 증명을 POST로 받아 HttpOnly 세션 생성
* `download.php`: 세션의 NAS 위치·카메라 경로 범위 안 MP4만 다운로드
* `logout.php`: NAS 세션 폐기 후 웹 포털로 복귀
* `upload.php`: 장치 세션 범위 안에서 생성·offset 조회·4MiB 조각 쓰기·완료 검증

업로드 임시 파일은 카메라 `temp/dfblackbox-uploads`에 두고 전체 길이와 SHA-256이
일치할 때만 `recordings` 아래 최종 MP4로 변경한다. 직접 폴더 탐색, 임의 삭제,
기존 파일 덮어쓰기와 녹화본 웹 재생은 제공하지 않는다.
