# 작업명

QR 승인 기반 기기 등록 및 NAS 폴더 프로비저닝

## 배경

DFBlackbox 장치를 서버에 등록하고, 웹 사용자가 승인한 장치와 카메라만 웹에서 조회·재생·다운로드할 수 있는 기반이 필요하다.

현재 저장소에는 Supabase 프로젝트, GitHub Pages 웹 프로젝트 및 NAS 원격 API가 포함되어 있지 않다. 따라서 클라우드가 사설망 NAS에 직접 접속하는 구조를 만들지 않고, 서버는 등록을 승인하고 실제 NAS 작업은 같은 네트워크의 DFBlackbox가 수행한다.

## 확정된 사용자 흐름

1. DFBlackbox에서 `기기 등록` 버튼을 누른다.
2. DFBlackbox가 서버에서 일회용 등록 요청을 발급받고 QR 코드와 등록코드를 표시한다.
3. 관리자가 휴대전화로 QR에 접속한다.
4. 관리자가 웹에 로그인하고 조직·설치 장소를 확인한 뒤 등록을 승인한다.
5. DFBlackbox는 2~3초 간격의 제한된 폴링으로 승인 결과를 수신한다.
6. 서버는 `device_id`, `camera_id`, NAS 상대 경로와 최초 1회용 장치 토큰을 반환한다.
7. DFBlackbox가 NAS SMB 공유에 카메라 폴더를 생성하고 쓰기 시험을 수행한다.
8. DFBlackbox가 서버에 `active/ready` 또는 `active/storage_error` 결과를 보고한다.

사용자가 보는 과정은 `기기 등록 → QR 접속 → 로그인 → 승인 → 완료`로 유지한다.

## 확정된 설계 기준

### 등록과 인증

* 초기 버전에서는 SMS OTP를 사용하지 않는다.
* 웹의 기존 Supabase 로그인으로 승인자를 식별한다.
* QR에는 `claim_code` 또는 등록 URL만 넣고 비밀번호, NAS 계정, Supabase 비밀키 및 장치 토큰을 넣지 않는다.
* 등록 요청은 일회용이며 기본 만료시간은 5분으로 한다.
* 장치 토큰은 승인 결과에서 최초 한 번만 반환한다.
* 장치 토큰은 Windows DPAPI로 현재 Windows 사용자 또는 장치 범위에 암호화해 저장하고 로그에 출력하지 않는다.
* Supabase secret/service key는 DFBlackbox와 GitHub Pages에 포함하지 않는다.

### 승인 결과 수신

* 최초 구현은 Supabase Realtime 대신 2~3초 폴링을 사용한다.
* 상태는 `pending`, `approved`, `rejected`, `expired`로 구분한다.
* 등록 버튼을 다시 누르면 이전의 미완료 폴링과 요청을 취소한다.
* 폼 종료 및 앱 종료 시 폴링을 즉시 취소한다.

### NAS 프로비저닝

* 클라우드 웹서버가 NAS에 직접 접속하지 않는다.
* NAS와 같은 네트워크의 DFBlackbox가 기존에 설정된 SMB 경로와 자격 증명을 이용해 폴더를 생성한다.
* 폴더명은 사용자가 입력한 카메라 이름 대신 서버가 발급한 안정적인 ID를 사용한다.
* `Directory.CreateDirectory` 기반의 멱등 작업으로 구현한다.
* 폴더 생성 후 작은 시험 파일을 생성·삭제해 실제 쓰기 권한을 확인한다.
* NAS 작업 실패는 서버 등록을 취소하지 않고 `storage_error`로 보고하며 재시도할 수 있어야 한다.

권장 상대 경로:

```text
DFBlackbox/{organization_id}/{site_id}/{device_id}/{camera_id}/
  live/
  recordings/
  events/
  temp/
```

### 장치와 카메라 관계

* 등록 최상위 단위는 DFBlackbox가 실행되는 `device`이다.
* 카메라는 `device`의 하위 자원이다.
* 현재 애플리케이션은 단일 카메라 구조이므로 최초 등록 시 현재 카메라 하나를 함께 등록한다.
* 한 프로세스에서 카메라 세 대를 동시에 처리하는 다중 `CameraSession` 개편은 이 작업 범위에서 제외한다.

## 최소 서버 계약

서버 구현 세부 기술과 무관하게 다음 의미를 유지한다.

### 등록 요청 생성

```http
POST /device-claims
```

요청에는 앱 버전, 설치 식별자, 현재 카메라 종류처럼 민감하지 않은 최소 정보만 포함한다.

응답 예시:

```json
{
  "claim_id": "uuid",
  "claim_code": "H7K4P9Q2",
  "approval_url": "https://example/register?code=H7K4P9Q2",
  "expires_at": "2026-08-12T12:05:00Z"
}
```

### 웹 승인

```http
POST /device-claims/{claim_code}/approve
```

* 로그인한 관리자의 사용자 JWT가 필요하다.
* 서버는 조직과 설치 장소에 대한 등록 권한을 검사한다.

### 승인 결과 조회

```http
GET /device-claims/{claim_id}/status
```

승인 응답 예시:

```json
{
  "status": "approved",
  "device_id": "dev_uuid",
  "camera_id": "cam_uuid",
  "device_token": "returned-once",
  "nas_relative_path": "DFBlackbox/org_uuid/site_uuid/dev_uuid/cam_uuid"
}
```

### 프로비저닝 결과 보고

```http
POST /devices/{device_id}/provisioning-result
```

```json
{
  "registration_state": "active",
  "storage_state": "ready",
  "error_code": null
}
```

## 최소 데이터 모델

* `device_claims`: 등록 요청, 코드 해시, 상태, 만료시각, 승인자
* `devices`: 조직, 장소, 장치 이름, 버전, 장치 자격 증명 해시, 마지막 접속시각, 폐기상태
* `cameras`: 장치, 카메라 종류, 사용자 표시명, 연결상태, 저장소상태

웹의 사용자별 카메라 접근 권한은 후속 작업에서 `camera_members` 또는 조직 역할 정책으로 추가한다.

## 구현 단계

### 1단계: 저장소 경계와 계약 확정

* 기존 설정 저장, 현지화, 메인 UI 구조를 분석한다.
* 현재 저장소 밖에 있는 Supabase 및 웹 구현을 임의로 가정하지 않는다.
* 서버 계약 DTO와 인터페이스 경계를 먼저 정의한다.
* 새 설정 필드는 기존 `settings.json` 하위 호환성을 유지하는 선택 필드로만 추가한다.

### 2단계: DFBlackbox 등록 클라이언트

* 등록 요청 생성·상태 조회·취소를 담당하는 서비스를 UI에서 분리한다.
* 네트워크 요청에 timeout과 `CancellationToken`을 적용한다.
* 최초 장치 토큰을 DPAPI로 암호화해 저장한다.
* 민감정보와 전체 승인 URL을 운영 로그에 기록하지 않는다.

### 3단계: 등록 UI

* 설정창 또는 상시 접근 가능한 적절한 위치에 `기기 등록` 진입점을 추가한다.
* QR, 등록코드, 만료시간, 대기·완료·실패 상태를 표시한다.
* QR 라이브러리 추가가 필요하면 기존 패키지 및 배포 영향을 먼저 검토한다.
* KOR/ENG 현지화를 함께 적용한다.

### 4단계: NAS 프로비저닝

* 서버가 반환한 상대 경로가 루트 밖으로 벗어나지 못하도록 검증한다.
* 폴더 생성과 쓰기 시험을 별도 서비스로 구현한다.
* 실패 사유는 정규화된 오류 코드로 서버와 UI에 보고한다.
* 비밀번호와 전체 NAS 경로는 로그에 남기지 않는다.

### 5단계: 서버·웹 연결

* Supabase 프로젝트 또는 별도 웹 저장소가 준비된 뒤 Edge Function, RLS 및 승인 페이지를 구현한다.
* 브라우저에는 publishable key만 사용하고 모든 공개 테이블에 RLS를 적용한다.
* 실제 비밀값은 저장소에 커밋하지 않는다.

## 제외 범위

* SMS OTP 및 문자 발송 사업자 연동
* 다중 카메라 캡처 구조 개편
* 라이브 스트리밍 및 HLS 생성
* 녹화 파일 업로드·다운로드 구현
* NAS 관리 페이지, SMB 또는 FTP 포트의 인터넷 직접 공개
* GitHub Pages 및 Supabase의 실제 운영 배포

## 외부 입력이 필요한 항목

다음 값은 코드에 하드코딩하지 않고 구현 단계에서 사용자에게 확인한다.

* Supabase 프로젝트 URL과 publishable key
* 승인 웹사이트의 기본 URL
* NAS SMB 루트 경로와 자격 증명 보관 방식
* 조직·설치 장소 선택을 누가 생성하고 관리하는지

외부 값이 준비되지 않았다면 인터페이스와 로컬 모의 구현까지만 진행하고 가짜 운영 주소나 비밀값을 만들지 않는다.

## 완료 조건

* 사용자가 QR 승인 흐름을 5단계 이내로 이해할 수 있다.
* 승인 전에는 장치 자격 증명과 NAS 폴더가 생성되지 않는다.
* 승인 완료 후 동일 요청을 재처리해도 중복 장치나 잘못된 폴더가 생기지 않는다.
* NAS 실패가 기기 등록 상태와 분리되어 표시되고 재시도 가능하다.
* 앱 종료, 취소, 만료 및 네트워크 오류에서 작업과 자원이 정리된다.
* 기존 카메라 연결·녹화·재생 동작에 회귀가 없다.
* 빌드가 경고와 오류 없이 완료된다.

## 결과

* 2026-08-12 기준 1~4단계의 DFBlackbox 내부 경계를 구현했다.
* `settings.json`에는 등록 API 기본 URL, NAS SMB 루트, 비민감 등록 메타데이터만 선택 필드로 추가했다. 기존 설정 파일에 필드가 없어도 기본값으로 로드된다.
* 서버 계약 DTO와 `IDeviceRegistrationClient`를 정의하고, 10초 HTTP timeout 및 `CancellationToken`을 사용하는 HTTP 구현과 로컬 모의 구현을 추가했다.
* 등록 요청 생성 후 2~10초 범위의 설정 간격으로 `pending/approved/rejected/expired` 상태를 폴링하며, 취소·폼 종료 시 요청과 대기를 중단한다.
* 최초 장치 토큰은 `settings.json`과 로그에 남기지 않고 Windows DPAPI 현재 사용자 범위로 별도 암호화 파일에 최초 한 번만 저장한다.
* 승인된 NAS 상대 경로가 설정 루트를 벗어나지 못하도록 검증하고, `live/recordings/events/temp`를 멱등 생성한 뒤 시험 파일 생성·삭제로 쓰기 권한을 확인한다.
* NAS 준비 실패와 서버 결과 보고 실패를 기기 승인과 분리해 표시하며, 저장된 장치 정보와 토큰으로 NAS 준비 및 결과 보고만 재시도할 수 있다.
* 설정창에 KOR/ENG 기기 등록 페이지와 등록 상태창을 추가했다. 사용자 승인에 따라 .NET 8 호환 QRCoder 1.8.0을 추가해 승인 URL QR 이미지를 표시하며, 새 요청·폼 종료 시 이전 Bitmap을 해제한다. QR 렌더링에 실패해도 등록코드 복사와 승인 페이지 열기 및 등록 폴링은 계속 사용할 수 있다.
* Supabase·웹·DB 스키마·운영 주소·비밀값은 추가하지 않았고 외부 서비스에 실제 쓰기를 수행하지 않았다.
* `dotnet build DFBlackbox\DFBlackbox.csproj -c Release` 결과 경고 0개, 오류 0개로 완료했다.
* `dotnet publish DFBlackbox\DFBlackbox.csproj -c Release -o .publishcheck`도 성공해 self-contained 단일 파일 게시 구성을 확인했다.
* Supabase 프로젝트 URL과 publishable key가 제공되어 등록 API 기본 URL을 해당 프로젝트의 `device-registration` Edge Function 경로로 연결하고, 공개 키는 `settings.json` 선택 필드에서 입력받아 `apikey` 헤더로 전송하도록 확장했다. 실제 키 문자열은 소스와 문서에 기록하지 않았다.
* `supabase/migrations/202608120001_device_registration.sql`에 조직·멤버십·장소·등록요청·장치·카메라·프로비저닝 이력, 인덱스, RLS 및 원자적 등록 함수를 작성했다. 등록코드와 장치 토큰은 DB에 해시만 저장한다.
* 추가 패키지 없이 `supabase/functions/device-registration/index.ts`에 등록 생성·상태 조회·승인·거절·프로비저닝 결과 보고 라우터를 작성했다. 새 publishable key는 `apikey`로 검증하고 사용자 승인은 별도 Supabase Auth JWT를 요구한다.
* 승인 웹 기본 URL, 조직/장소 초기 데이터와 원격 DB 적용 권한은 아직 제공되지 않아 마이그레이션 적용 및 Edge Function 배포는 수행하지 않았다.
* 설정 팝업의 기기 등록 페이지에서는 API URL·publishable key·NAS 루트 입력란을 제거했다. Supabase 프로젝트 URL과 공개 키는 `DeviceRegistrationSettings` 기본값으로 새 `settings.json`에 저장되며, 실제 NAS SMB 루트만 배포 환경의 `settings.json`에서 입력한다.
* 사용자가 Supabase 원격 프로젝트에 `db push`를 완료했다. 조직·장소·최초 관리자 초기 데이터와 Edge Function/승인 웹 배포는 아직 필요하다.
* 전달된 ipDISK `guest.cgi` 주소는 브라우저용 HTTP 업로드 페이지이므로 현재의 파일시스템 기반 NAS 루트로 사용하지 않았다. 실제 SMB UNC 경로 또는 Windows에 마운트된 WebDAV 드라이브 경로가 필요하며, 전달된 NAS 계정 정보는 저장소와 문서에 기록하지 않았다.
* 이 PC의 ipDISK 가상 드라이브 `S:\HDD1\Media`에 대해 디렉터리 조회와 임시 파일 생성·삭제를 검증했고 모두 성공했다. 현재 Debug 실행본의 `settings.json`에 해당 경로를 NAS 루트로 반영했으며 계정과 비밀번호는 저장하지 않았다.
* 최초 관리자 Auth 사용자 UUID를 `MyCompany` 조직의 `owner`로 연결하고 `MyComputer` 설치 장소를 만드는 멱등 초기 데이터 마이그레이션 `202608120002_seed_mycompany.sql`을 추가했다. 관리자 이메일과 비밀번호는 마이그레이션에 기록하지 않았다.
* 사용자가 두 번째 원격 `db push`를 완료해 `MyCompany` 조직, `MyComputer` 설치 장소와 최초 owner 연결이 원격 DB에 적용되었다.
* GitHub Pages용 `web/` 승인 포털과 `.github/workflows/pages.yml` 배포 워크플로를 추가했다. 등록코드 접속 시 Supabase 로그인·조직/장소 확인·승인/거절을 제공하고, 일반 접속 시 등록 카메라의 연결 및 저장소 상태를 표시한다. 로그인 세션은 현재 브라우저 탭에만 보관하며 실제 스트리밍은 아직 연결하지 않았다.
* 사용자가 `스트리밍` 브랜치를 원격에 푸시했고 GitHub Pages 배포 완료를 확인했다. 승인 웹 기본 URL은 `https://ornithopter83.github.io/DF_REC_CAM/`, 허용 브라우저 origin은 `https://ornithopter83.github.io`로 확정했다.
* 사용자가 `APPROVAL_BASE_URL`과 `APPROVAL_ALLOWED_ORIGIN` 설정 후 `device-registration` Edge Function 배포를 완료했다.
* QRCoder 추가 후 Release 빌드는 경고 0개·오류 0개로 완료했고, self-contained 단일 파일 게시도 다시 성공했다.
* 별도 Dummy 등록 시험은 제거했다. 실제 카메라가 연결되지 않은 상태에서도 동일한 운영 Supabase 승인 흐름으로 등록할 수 있으며, 승인 결과는 실제 `settings.json` 장치 정보와 DPAPI 토큰 저장소에 반영된다.
* 사용자가 GitHub Pages 환경에 `스트리밍` 브랜치를 허용한 뒤 당시 Dummy 설치 ID로 승인 웹 접속, 기기 승인, DFBlackbox 결과 폴링과 NAS 폴더 생성을 확인했다. 서버·웹·NAS 연결은 검증됐지만 Dummy 제거 후 정상 설치 ID의 실제 등록은 별도로 확인해야 한다.
* Dummy 전용 로컬 DPAPI 토큰을 삭제하고, 원격 DB에서 `-dummy` 설치 ID에 해당하는 장치 1행·카메라 1행·승인 요청 2행·프로비저닝 보고 1행을 직접 정리했다. 후속 조회에서 Dummy 관련 행이 모두 0건임을 확인했으며 정상 설치 ID의 장치는 아직 등록되지 않은 상태다.
* Dummy NAS 경로에는 파일이 없었고 `live/recordings/events/temp` 하위 폴더를 제거했다. ipDISK 가상 드라이브가 빈 카메라 및 장치 상위 폴더 삭제 요청을 반영하지 않아 두 빈 폴더는 남아 있으며, ipDISK 전용 프로그램에서 수동 정리가 필요하다.
* 기존 UUID 4단계 NAS 경로는 초기 충돌 방지 설계였으며 NAS가 임의 생성한 구조가 아님을 확인했다. 사용자 요구에 따라 새 등록부터 `DFBlackbox/{조직명}/{공백 없는 카메라명}`을 사용하도록 `202608120003_simple_nas_path.sql` 마이그레이션을 추가했다. `MyCompany`와 기본 `Camera 1`은 `DFBlackbox/MyCompany/Camera1`이 되며 Windows 금지문자는 `_`로 치환한다. 원격 적용을 위한 `db push`는 아직 필요하다.
* 사용자가 `202608120003_simple_nas_path.sql`의 원격 `db push`를 완료했다. 정상 설치 ID로 실제 등록한 뒤 `S:\HDD1\Media\DFBlackbox\MyCompany\Camera1` 생성 여부를 확인하면 된다.
* 등록 완료 시 웹에서 입력한 등록명과 장치·카메라 ID, NAS 상대 경로, 등록 시각을 `HKCU\Software\DFBlackbox\DeviceRegistration`에 저장하고 설정의 기기 등록 페이지에 `등록됨: {등록명}`으로 표시하도록 추가했다. 장치 토큰은 기존 DPAPI 파일에만 유지한다.
* 설정에 `등록 해제`를 추가했다. 장치 토큰으로 서버 폐기 요청이 성공한 뒤 로컬 레지스트리·`settings.json` 등록 식별자·DPAPI 토큰을 정리하며 NAS 폴더와 녹화 파일은 보존한다. 같은 설치 ID의 재등록은 기존 장치·카메라 행을 안전하게 재활성화한다.
* `202608120004_device_registration_name_and_revoke.sql`에 등록명 반환, 토큰 기반 폐기, 재등록 함수를 추가하고 Edge Function에 승인 v2·상태 v2·`POST /devices/{device_id}/revoke`를 연결했다. `supabase db push --dry-run`에서 004 마이그레이션이 유일한 적용 대상으로 정상 인식됐으며 실제 `db push`와 Edge Function 재배포는 아직 필요하다.
* 등록과 NAS 준비가 완료되면 등록 팝업을 닫았다 다시 열지 않아도 즉시 완료 화면으로 전환한다. QR·등록코드·만료시간·승인 관련 버튼을 숨기고 등록명과 NAS 상대 경로 및 `닫기` 버튼을 표시하며, 완료 화면 전환 전에 `settings.json`과 HKCU 등록 정보를 저장한다.
* 승인 웹이 로그인 후 등록코드 상태를 조회하도록 `GET /device-claims/{claim_code}`와 `inspect_device_claim` DB 함수를 추가했다. `pending`일 때만 승인 폼을 표시하고 `approved/rejected/expired/not_found`는 읽기 전용 종료 화면으로 전환해 처리된 승인 URL을 다시 사용할 수 없게 했다.
* 적용 완료된 004 마이그레이션은 변경하지 않고 `202608120005_claim_inspection.sql`을 별도로 추가했다. `supabase db push --dry-run`에서 005만 적용 대상으로 확인했으며, 실제 DB push·Edge Function 재배포·Pages 재배포는 아직 필요하다.
* 2026-08-14 작업 21에서 신규 PC 등록의 로컬 SMB 루트 사전검사와 SMB 폴더 생성을 폐기했다. 승인 후에는 장치 토큰으로 Edge 범위 세션을 발급받고 NAS HTTPS Gateway가 카메라 기본 폴더를 생성·쓰기 검사한다.

## 새 스레드 시작 명령서

```text
C:\Projects\VS\DFBlackbox에서 작업해.

AGENTS.md, docs/PROJECT_CONTEXT.md, docs/DECISIONS.md와
tasks/13_QR_기기등록_NAS_프로비저닝.md를 먼저 모두 읽고 13번 작업을 진행해.

먼저 현재 설정 저장, 현지화, MainForm/SettingsForm, 네트워크 서비스 구조와 작업 트리의 기존 변경을 분석해. 그 다음 13번 문서의 1단계부터 구현하되, 저장소에 Supabase·웹 프로젝트나 실제 외부 설정이 없다면 운영 주소나 비밀값을 임의로 만들지 말고 인터페이스/DTO, 안전한 로컬 저장 경계, 모의 가능한 등록 흐름까지 진행해. 외부 서비스에 쓰기, DB 스키마 적용, 패키지 추가처럼 사용자 확인이 필요한 변경은 영향과 이유를 먼저 보고해.

QR 등록의 확정 흐름은 기기 등록 → QR 웹 접속 → 로그인 → 승인 → DFBlackbox 결과 폴링 → DFBlackbox가 NAS 폴더 생성이다. 클라우드가 NAS에 직접 접속하게 만들지 말고, SMS OTP·다중 카메라·스트리밍 구현은 이번 범위에서 제외해.

기존 카메라 연결·녹화·재생 동작을 보존하고, 작업 후 빌드/검증 및 task 결과 갱신까지 완료해.
```
