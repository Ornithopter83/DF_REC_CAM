# 녹화 미디어 API 계약

기본 URL은 `{SUPABASE_URL}/functions/v1/recording-media/`이다. 모든 요청은 프로젝트 publishable key를 `apikey` 헤더로 보낸다. 장치 경로는 DPAPI에 보관된 장치 토큰을, 사용자 경로는 Supabase Auth access token을 `Authorization: Bearer`로 보낸다.

대용량 MP4는 Supabase를 통과하지 않고 NAS HTTPS Gateway로 직접 전송한다. 이 함수는 권한 확인, 범위 제한 assertion과 카탈로그 메타데이터만 처리한다.

## 장치 경로

| HTTP | 역할 |
| --- | --- |
| `POST devices/{device_id}/nas-upload-session` | 등록 장치·카메라·NAS 위치를 확인하고 1시간 업로드 assertion 발급 |
| `POST devices/{device_id}/recordings/catalog` | NAS 확정 MP4의 메타데이터를 SHA-256 멱등 키로 등록 |

NAS 세션 응답에는 HTTPS Gateway 기준 URL, `provision.php`, `upload.php`, 카메라 NAS 상대 경로, 4MiB 조각 크기와 assertion이 포함된다. 장치 토큰은 NAS에 전달하지 않는다.

카탈로그 경로는 등록된 카메라 루트 아래 `recordings/*.mp4`여야 한다. 같은 장치·카메라·SHA-256 요청은 동일 항목을 갱신하는 멱등 동작이다.

## 사용자 경로

| HTTP | 역할 |
| --- | --- |
| `GET portal/cameras` | 조직 권한 내 카메라, 장치 온라인 상태와 NAS 상태 조회 |
| `POST nas-session` | 조직별 NAS 다운로드 범위를 RS256 assertion으로 발급 |
| `GET cameras/{camera_id}/recordings` | `ready` NAS 녹화 카탈로그를 cursor 기반 최신순 조회 |
| `POST recordings/{recording_id}/download-url` | 권한 확인 후 NAS `download.php` HTTPS 주소 반환 |

웹은 `nas-session` assertion을 NAS `auth.php`에 POST해 8시간 HttpOnly 세션으로 교환한다. 다운로드 URL에는 녹화 ID, NAS 위치 ID와 상대 경로만 포함되며 NAS 계정·비밀번호·서명값은 포함하지 않는다.

## 제공하지 않는 기능

- Supabase Storage 업로드와 TUS 세션
- 녹화본 signed URL 재생
- 대용량 파일 프록시
- NAS 디렉터리 목록·삭제

웹 내 영상 재생은 `media-session`의 LiveKit 실시간 스트리밍만 사용한다.
