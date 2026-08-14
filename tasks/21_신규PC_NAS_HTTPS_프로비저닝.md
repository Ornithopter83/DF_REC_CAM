# 작업명

신규 PC의 NAS HTTPS 기기 등록과 프로비저닝

## 목표

새 PC에서 ipDISK Drive·SMB 루트 없이 기기 등록을 시작하고, 승인된 장치·카메라 범위의 NAS HTTPS 요청으로 카메라 기본 폴더를 준비한 뒤 `settings.json`과 DPAPI 토큰 저장을 완료한다.

## 세부 작업

* A. 설정 화면의 NAS SMB 사전검사를 제거하고 등록 워크플로를 HTTPS 프로비저닝 계약으로 전환한다. (완료: 2026-08-14)
* B. Edge 업로드 세션과 NAS Gateway에 범위 제한 HTTPS 프로비저닝을 추가한다. (완료: 2026-08-14)
* C. Edge·NAS 배포, 신규 PC 조건 계약, Release 빌드·publish와 회귀 검사를 수행한다. (완료: 2026-08-14)

## 진행 현황

잔여 작업 0개

## 정책·보안 경계

* 새 PC에는 기존 PC의 `settings.json`, DPAPI 토큰 또는 레지스트리 등록값을 복사하지 않는다.
* 장치 토큰은 Edge에만 전송하고 NAS에는 카메라 경로 범위가 서명된 assertion만 전송한다.
* NAS 프로비저닝은 서명된 카메라 루트와 `live`, `recordings`, `events`, `temp` 폴더 생성·쓰기 확인만 허용한다.
* 클라우드는 NAS에 직접 접속하지 않으며 대용량 파일은 Supabase를 통과하지 않는다.
* 기존 SMB 설정 필드는 `settings.json` 하위 호환을 위해 유지하되 신규 등록의 필수조건으로 사용하지 않는다.

## 결과

* A: 기기 등록 진입 전에 로컬 NAS 루트의 절대경로·접근 가능성을 검사하던 경고를 제거했다. 기존 `NasRootFolder` 설정 필드는 하위 호환을 위해 유지하지만 신규 등록에는 사용하지 않는다.
* A: 승인 후와 NAS 준비 재시도 모두 DPAPI 장치 토큰으로 Edge에서 카메라 범위 세션을 발급받고 NAS HTTPS 프로비저닝을 호출하도록 데스크톱 워크플로를 전환했다.
* B: Edge의 장치 업로드 세션에 등록된 NAS 상대 경로와 `provision.php` URL을 추가했다. 기존 장치 토큰·카메라 소유권·NAS 위치 검증과 1시간 RS256 assertion을 그대로 재사용한다.
* B: NAS `provision.php`는 assertion의 카메라 경로를 세그먼트별로 생성하면서 symlink와 루트 이탈을 거부하고, `live`, `recordings`, `events`, `temp` 생성과 임시 쓰기 검사를 수행한다. 목록·삭제·임의 경로 입력은 제공하지 않는다.
* C: `recording-media`와 NAS `provision.php`를 배포했다. 무세션 NAS 요청과 잘못된 장치 토큰의 Edge 요청은 모두 401로 거부되었다.
* C: 현재 등록 장치의 DPAPI 토큰을 메모리에서만 사용해 HTTPS 준비를 호출한 결과 Edge 세션 200, NAS `ready`, 상대 경로 일치, 표준 폴더 누락 0개와 쓰기 검사 잔여파일 0개를 확인했다.
* C: 신규 기본 설정의 빈 `NasRootFolder`가 등록 진입을 차단하는 코드와 승인 후 로컬 파일시스템 프로비저닝 호출이 남아 있지 않음을 확인했다. 실제 두 번째 PC의 신규 승인 자체는 새 서버 장치가 생성되는 운영 동작이므로 이번 PC에서는 수행하지 않았다.
* C: Release 빌드는 경고 0개·오류 0개였고 self-contained 단일 파일 publish가 성공했다. DB 스키마·패키지·비용 설정은 변경하지 않았다.
* C: 기존 `INasProvisioningService`, `NasProvisioningService`와 등록 폼의 SMB 생성자 계약은 외부 호출 호환용으로 유지하되 실제 MainForm 신규 등록 경로는 HTTPS 구현만 생성하도록 확인했다.
