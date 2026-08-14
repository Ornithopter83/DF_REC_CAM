# 녹화 미디어 API 계약

기본 URL은 `{SUPABASE_URL}/functions/v1/recording-media`이다. 모든 요청은 프로젝트의
publishable key를 `apikey` 헤더로 보낸다. 장치 경로는 DPAPI에 보관된 장치 토큰을,
사용자 경로는 Supabase Auth access token을 `Authorization: Bearer ...`로 보낸다.

대용량 MP4는 NAS에만 저장한다. 이 API는 파일을 업로드하거나 프록시하지 않으며 NAS
식별자와 상대 경로로 구성된 카탈로그만 관리한다.

## 장치: NAS 녹화 카탈로그 등록

`POST /devices/{device_id}/recordings/catalog`

```json
{
  "camera_id": "uuid",
  "nas_relative_path": "DFBlackbox/MyCompany/Camera1/recordings/2026/08/12/recording.mp4",
  "original_file_name": "recording.mp4",
  "file_size_bytes": 123456789,
  "source_fingerprint": "lowercase-sha256-hex",
  "recorded_at": "2026-08-12T12:34:56Z",
  "duration_seconds": 600.25,
  "source_last_modified_at": "2026-08-12T12:45:00Z"
}
```

경로는 등록된 카메라의 NAS 상대 경로 아래 `recordings/`에 있어야 한다. 같은
장치·카메라·SHA-256 요청은 멱등이며 응답은 다음과 같다.

```json
{
  "recording_id": "uuid",
  "catalog_state": "ready"
}
```

## 사용자: 목록과 NAS 다운로드

- `GET /cameras/{camera_id}/recordings?limit=50&cursor={opaque}`
- `POST /recordings/{recording_id}/download-url`

목록은 로그인 사용자가 속한 조직의 NAS 카탈로그 항목만 최신순으로 반환한다.
다운로드 API는 권한 확인 후 HTTPS NAS `list` 주소를 반환한다.

```json
{
  "download_url": "https://nas.example/list/HDD1/share/path/recording.mp4",
  "file_name": "recording.mp4",
  "authentication_required": true
}
```

웹은 NAS 계정이나 비밀번호를 저장하지 않는다. 사용자가 NAS 최상위 탭에서 로그인한
브라우저 세션으로 다운로드하며, 인증되지 않은 요청은 NAS 로그인 HTML을 반환할 수 있다.
웹 재생 URL과 Supabase Storage signed URL은 제공하지 않는다.
