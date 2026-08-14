# DFBlackbox Supabase 백엔드

이 디렉터리는 QR 기기 등록, 조직 권한, LiveKit 시청 lease, NAS 위치와 녹화 카탈로그에 필요한 PostgreSQL 마이그레이션과 Edge Function 설정을 관리한다. 원격 프로젝트에는 마이그레이션 001~015가 적용되어 있다.

## 마이그레이션 구성

| 범위 | 역할 |
| --- | --- |
| 001~005 | 조직·사이트·장치·카메라·등록 요청, 승인명, 등록 해제와 요청 조회 |
| 006~008 | LiveKit Ingress 구성, 시청 lease, 장치 명령·송출 상태와 수명주기 보호 |
| 009~010 | NAS 위치, 카메라 NAS 경로, 녹화 카탈로그와 장치 인증 계약 |
| 011 | 웹 NAS SSO 범위, 장치 공인 IP·heartbeat와 포털 조회 |
| 012 | 장치별 NAS HTTPS 업로드 범위 조회 |
| 013 | 신규 카메라 표시명과 NAS 경로를 등록 PC명으로 생성 |
| 014 | NAS 준비 전 `approved` 장치의 범위 제한 세션 발급 허용 |
| 015 | 신규 NAS 카메라 폴더에서 등록 PC명의 내부 공백 보존 |

모든 공개 테이블은 RLS를 사용한다. 로그인 사용자는 자신이 활성 멤버인 조직 범위만 조회하며 장치용 원자적 RPC는 service-role 전용이다. 등록코드와 장치 토큰 평문은 DB에 저장하지 않고 SHA-256 해시만 보관한다.

## Edge Function

| 함수 | 역할 |
| --- | --- |
| `device-registration` | 등록 요청 생성·조회, 관리자 승인·거절, NAS 준비 결과 보고, 등록 해제 |
| `media-session` | LiveKit 시청 lease·참가 토큰, 장치 송출 명령·상태 보고 |
| `recording-media` | 포털 카메라·녹화 목록, NAS SSO assertion, 장치 NAS 세션·카탈로그 등록, 다운로드 URL |

`supabase/config.toml`은 세 함수 모두 `verify_jwt = false`로 배포한다. 각 함수가 공개 publishable key, 사용자 JWT 또는 장치 토큰을 경로별로 직접 검증한다.

## 비밀값과 환경 변수

값 자체는 저장소에 기록하지 않는다.

- 공통: `SUPABASE_URL`, publishable key 목록, Supabase secret key
- 등록: `APPROVAL_BASE_URL`, `APPROVAL_ALLOWED_ORIGIN`
- LiveKit: `LIVEKIT_URL`, `LIVEKIT_API_KEY`, `LIVEKIT_API_SECRET`
- NAS SSO·업로드 서명: `NAS_SESSION_PRIVATE_KEY`

opaque `sb_secret_` 형식의 Supabase 관리자 키는 REST RPC의 `apikey` 헤더로만 전송한다. 사용자 JWT만 `Authorization: Bearer`에 넣는다.

## 데이터 정책

- 모든 대용량 MP4는 NAS에만 저장한다.
- DB에는 NAS 식별자, 상대 경로, 파일명, 크기, 녹화시각, SHA-256 멱등 식별자 등 메타데이터만 저장한다.
- 기존 비공개 Storage 버킷과 레거시 열은 이미 배포된 호환 상태이지만 새 녹화 업로드에는 사용하지 않는다.
- 기존 Storage 객체는 별도 승인 없이 삭제하지 않는다.

## 적용과 검증

```powershell
supabase migration list
supabase db push --dry-run
supabase db push
supabase functions deploy device-registration
supabase functions deploy media-session
supabase functions deploy recording-media
```

원격 스키마 변경 전 적용 대상과 영향을 먼저 보고하고 SQL Editor 직접 수정 대신 마이그레이션 파일을 사용한다. 프로젝트 참조, DB 비밀번호와 실제 비밀값은 문서나 커밋에 포함하지 않는다.
