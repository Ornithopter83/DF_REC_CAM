# 녹화 미디어 API 계약

기본 URL은 `{SUPABASE_URL}/functions/v1/recording-media`이다. 모든 요청은 프로젝트의
publishable key를 `apikey` 헤더로 보낸다. 장치 경로는 DPAPI에 보관된 장치 토큰을,
사용자 경로는 Supabase Auth access token을 `Authorization: Bearer ...`로 보낸다.

## 장치: NAS 파일 등록 및 업로드 세션

`POST /devices/{device_id}/recordings/upload-session`

```json
{
  "camera_id": "uuid",
  "source_relative_path": "2026/08/12/recording.mp4",
  "original_file_name": "recording.mp4",
  "file_size_bytes": 123456789,
  "source_fingerprint": "lowercase-sha256-hex",
  "recorded_at": "2026-08-12T12:34:56Z",
  "duration_seconds": 600.25,
  "source_last_modified_at": "2026-08-12T12:45:00Z"
}
```

`source_relative_path`는 카메라의 NAS `recordings` 폴더를 기준으로 하며 절대경로나
`..`를 허용하지 않는다. `source_fingerprint`는 완성된 MP4 전체의 SHA-256이다.
DFBlackbox가 만든 파일과 NAS에 수동으로 추가된 파일 모두 같은 요청을 사용한다.
같은 장치·카메라·SHA-256 요청은 멱등이다.

이미 준비된 파일:

```json
{
  "recording_id": "uuid",
  "sync_state": "ready",
  "upload_required": false
}
```

새 업로드가 필요한 파일:

```json
{
  "recording_id": "uuid",
  "sync_state": "uploading",
  "upload_required": true,
  "bucket": "dfblackbox-recordings",
  "object_path": "organization/camera/recording.mp4",
  "tus": {
    "endpoint": "https://project-ref.storage.supabase.co/storage/v1/upload/resumable",
    "signature": "short-lived-token",
    "signature_expires_at": "ISO-8601",
    "chunk_size_bytes": 6291456,
    "metadata": {
      "bucketName": "dfblackbox-recordings",
      "objectName": "organization/camera/recording.mp4",
      "contentType": "video/mp4",
      "cacheControl": "3600"
    }
  }
}
```

## 패키지 없는 .NET TUS 전송

1. `POST {tus.endpoint}`로 세션을 만든다.
2. 다음 헤더를 보낸다.
   - `Tus-Resumable: 1.0.0`
   - `Upload-Length: {전체 바이트 수}`
   - `x-signature: {tus.signature}`
   - `Upload-Metadata: bucketName {base64 UTF-8},objectName {base64 UTF-8},contentType {base64 UTF-8},cacheControl {base64 UTF-8}`
3. `201 Created` 응답의 `Location`을 재개 URL로 보관한다.
4. 재시도 전 `HEAD {Location}`에 `Tus-Resumable`과 `x-signature`를 보내
   `Upload-Offset`을 확인한다.
5. `PATCH {Location}`에 최대 6 MiB씩 보낸다. 헤더는 다음과 같다.
   - `Tus-Resumable: 1.0.0`
   - `Upload-Offset: {현재 오프셋}`
   - `Content-Type: application/offset+octet-stream`
   - `x-signature: {tus.signature}`
6. `204 No Content`의 새 `Upload-Offset`이 전체 길이가 될 때까지 반복한다.

동일 `Location`에는 동시에 한 요청만 보낸다. 서명 만료 또는 업로드 URL 만료 시
업로드 세션 API를 다시 호출한다. Storage 객체 경로는 UUID 기반이며 덮어쓰기를
허용하지 않는다.

## 장치: 완료 확인

`POST /devices/{device_id}/recordings/{recording_id}/complete`

```json
{
  "file_size_bytes": 123456789,
  "source_fingerprint": "lowercase-sha256-hex"
}
```

Edge Function이 service secret으로 비공개 Storage 객체를 조회하고 실제 바이트 길이가
일치할 때만 DB 상태를 `ready`로 바꾼다. 객체가 아직 없거나 길이가 다르면 `409`이며
장치는 전송을 재개하거나 다시 시도할 수 있다.

## 사용자: 목록과 접근 URL

- `GET /cameras/{camera_id}/recordings?limit=50&cursor={opaque}`
- `POST /recordings/{recording_id}/play-url`
- `POST /recordings/{recording_id}/download-url`

목록은 로그인 사용자가 속한 조직의 `ready` 파일만 최신순으로 반환한다.

```json
{
  "items": [
    {
      "id": "uuid",
      "camera_id": "uuid",
      "original_file_name": "recording.mp4",
      "recorded_at": "ISO-8601",
      "duration_seconds": 600.25,
      "file_size_bytes": 123456789
    }
  ],
  "next_cursor": null
}
```

재생과 다운로드 URL은 5분간 유효하다. NAS 절대경로와 Storage 객체 경로는 사용자
응답에 포함하지 않는다.
