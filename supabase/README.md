# DFBlackbox Supabase 등록 백엔드

이 디렉터리는 QR 승인 기반 기기 등록에 필요한 데이터베이스 마이그레이션을 관리한다. 원격 프로젝트에는 아직 적용하지 않았다.

## 구성

`migrations/202608120001_device_registration.sql`은 다음 객체를 만든다.

| 객체 | 역할 |
| --- | --- |
| `organizations` | 장치와 사용자가 속한 조직 |
| `organization_members` | Supabase Auth 사용자와 조직 역할(`owner/admin/viewer`) |
| `sites` | 조직 내 설치 장소 |
| `devices` | DFBlackbox 설치 단위, 장치 토큰 해시와 등록/저장소 상태 |
| `cameras` | 장치에 속한 카메라. 현재 앱은 장치당 첫 카메라 하나를 사용 |
| `device_claims` | 5분짜리 일회용 등록 요청. 등록코드는 SHA-256 해시만 저장 |
| `device_provisioning_reports` | NAS 준비 결과 이력 |

모든 공개 테이블은 RLS를 활성화한다. 로그인한 사용자는 자신이 활성 멤버인 조직의 조직·장소·장치·카메라·프로비저닝 결과만 조회할 수 있다. `anon` 역할에는 테이블 권한을 부여하지 않는다. 등록 요청 생성과 상태 소비 및 프로비저닝 보고 함수는 `service_role`만 실행할 수 있다.

## 원자적 함수

| 함수 | 호출 주체 | 동작 |
| --- | --- | --- |
| `create_device_claim` | Edge Function | 등록코드 해시와 만료시각 저장 |
| `approve_device_claim` | 로그인한 `owner/admin` | 장치·카메라를 멱등 생성하고 NAS 상대 경로 확정 |
| `reject_device_claim` | 로그인한 `owner/admin` | 대기 중 요청 거절 |
| `consume_device_claim` | Edge Function | 상태 조회 및 최초 한 번만 장치 토큰 발급 |
| `report_device_provisioning` | Edge Function | 장치 토큰 검증 후 `ready/storage_error` 반영 |

장치 토큰 평문은 테이블에 저장하지 않는다. 최초 승인 결과를 소비할 때 256비트 토큰을 생성하고 `devices.token_hash`에 SHA-256 해시만 저장한다. 동일 설치가 다시 등록되더라도 기존 토큰을 임의로 교체하지 않는다.

## 필요한 Edge Function 경계

DFBlackbox의 등록 API 기본 URL은 다음 단일 Edge Function을 기준으로 한다.

```text
https://ujttgkmwqdwxevnbblvb.supabase.co/functions/v1/device-registration/
```

Edge Function은 아래 경로를 라우팅해야 한다.

| HTTP | 내부 처리 |
| --- | --- |
| `POST device-claims` | 안전한 8자리 코드를 생성하고 `create_device_claim` 호출 |
| `GET device-claims/{claim_id}/status` | `consume_device_claim` 호출 |
| `POST device-claims/{claim_code}/approve` | 사용자 JWT로 `approve_device_claim` 호출 |
| `POST devices/{device_id}/provisioning-result` | Bearer 장치 토큰으로 `report_device_provisioning` 호출 |

새 형식의 publishable key는 JWT가 아니므로 DFBlackbox는 `apikey` 헤더로 전송한다. 공개 장치 요청을 받는 Edge Function은 `verify_jwt = false`로 배포하고, 함수 내부에서 허용된 publishable key인지 검증하며 등록 생성 요청에 속도 제한을 적용해야 한다. 승인 경로는 별도로 사용자 JWT를 검증해야 한다.

승인 URL을 만들기 위한 `APPROVAL_BASE_URL`은 아직 확정되지 않았다. Edge Function 환경 변수로 설정하고 저장소에 직접 기록하지 않는다. Secret/service-role 키도 Edge Function 환경에서만 사용한다.

## 초기 데이터

마이그레이션은 조직이나 관리자 계정을 임의로 만들지 않는다. Supabase Auth 사용자가 준비된 후 신뢰된 관리 경로에서 다음 순서로 초기 데이터를 넣어야 한다.

1. `organizations`에 조직 생성
2. `organization_members`에 해당 Auth 사용자 ID를 `owner`로 연결
3. `sites`에 설치 장소 생성

이 초기 작업을 브라우저의 publishable key로 직접 허용하지 않는다.

## 적용 전 확인

원격 적용은 DB 스키마를 변경하므로 별도 승인과 프로젝트 인증이 필요하다. 적용 전 원격 마이그레이션 이력을 먼저 확인하고, SQL Editor에서 직접 수정하지 말고 마이그레이션으로 적용한다.

```powershell
supabase link --project-ref ujttgkmwqdwxevnbblvb
supabase migration list
supabase db push
```

로컬 Supabase 환경이 준비된 경우 먼저 `supabase db reset --local`로 마이그레이션을 검증한다. 실제 키와 DB 비밀번호는 `.env`나 문서에 커밋하지 않는다.
