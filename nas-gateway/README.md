# DFBlackbox NAS Gateway

NAS1DUAL Apache/PHP 7.3 환경에서 웹 NAS SSO, 녹화 다운로드, 신규 카메라 폴더 준비와 장치 MP4 이어올리기를 제공한다.

## 배포 위치

저장소의 `nas-gateway/` 내용을 NAS의 `/HDD1/DocRoot/dfblackbox`에 복사한다. 현재 Apache PHP 경로는 `/mnt/HDD1/DocRoot/dfblackbox`, 미디어 루트는 `/mnt/HDD1/Media`다.

`config.php`에는 Gateway·포털 URL, 세션 제한, 미디어 루트와 assertion 검증용 RSA 공개키만 둔다. RSA 개인키, Supabase secret key와 NAS 계정 정보는 NAS나 저장소에 기록하지 않는다.

## 공개 엔드포인트

| 파일 | 역할 |
| --- | --- |
| `auth.php` | Edge가 서명한 사용자 범위를 8시간 HttpOnly NAS 세션으로 교환 |
| `download.php` | 세션의 NAS 위치·카메라 범위 안 `recordings/*.mp4`만 attachment·Range 다운로드 |
| `logout.php` | NAS 세션 폐기 후 포털 복귀 |
| `provision.php` | 장치 assertion의 카메라 루트와 `live`, `recordings`, `events`, `temp` 생성·쓰기 검사 |
| `upload.php` | 업로드 생성·offset 조회·4MiB 조각 쓰기·길이와 SHA-256 완료 검증 |

## 파일 정책

- 업로드 임시 상태는 카메라 `temp/dfblackbox-uploads` 아래에 둔다.
- 동일 세션은 서버 offset부터 이어서 전송한다.
- 전체 길이와 SHA-256이 일치할 때만 같은 볼륨의 최종 `recordings/*.mp4`로 원자적 변경한다.
- 기존 최종 파일 덮어쓰기, 임의 경로 입력, 폴더 탐색과 삭제를 제공하지 않는다.
- 녹화본은 웹 재생이 아니라 다운로드로만 제공한다.

## 보안 검사

- RS256 서명, issuer, audience, gateway URL, 만료시각과 UUID를 검증한다.
- 사용자 세션은 조직 권한에서 파생된 NAS 위치·카메라 prefix만 가진다.
- 장치 세션은 장치·카메라·NAS 위치와 한 개 카메라 prefix로 제한한다.
- 실제 파일 경로는 `realpath`로 미디어 루트 하위인지 확인하고 symlink 및 경로 이탈을 거부한다.
- `config.php`와 `common.php`는 Apache에서 직접 접근을 차단한다.
