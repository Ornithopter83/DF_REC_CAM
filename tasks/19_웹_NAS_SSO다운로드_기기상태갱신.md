# 작업명

웹·NAS 통합 로그인 다운로드와 기기 상태 갱신

## 목표

Supabase 웹 로그인 한 번으로 NAS1DUAL의 조직별 녹화 파일을 별도 NAS 계정 입력 없이 다운로드한다. 녹화 목록은 최초 로그인과 사용자 새로고침에서 DB 카탈로그를 다시 조회하고, 웹은 DB의 기기 IP·마지막 heartbeat를 기준으로 온라인 상태를 표시한다.

## 예정 구현 범위

* A. DB·Edge Function에 NAS 세션 생성용 서명 증명, 조직별 NAS 범위와 기기 공인 IP·heartbeat 계약을 추가한다. (완료: 2026-08-14)
* B. NAS1DUAL Apache/PHP 환경에 공개키 검증, HttpOnly 세션, 경로 제한, attachment·Range 다운로드 Gateway를 구현한다. (완료: 2026-08-14)
* C. 웹 로그인 후 NAS SSO 세션을 자동 생성하고 로그인·전체 새로고침에서 녹화 목록을 재조회하며 기기 상태를 주기 갱신한다. (완료: 2026-08-14)
* D. 원격 마이그레이션·Edge·NAS·Pages를 배포하고 기존 117MB MP4의 DB 목록·실제 다운로드를 검증한다. (완료: 2026-08-14)

## 진행 현황

잔여 작업 0개

* 세부 작업은 한 번에 하나만 진행하고 완료한 식별자를 잔여 목록에서 제거한다.
* 추가 발견 과제는 현재 단계에 임의로 포함하지 않고 별도 지시 대상으로 분리한다.

## 보안 경계

* NAS 세션 증명은 파일별 다운로드 티켓이 아니라 웹 로그인 사용자가 NAS 세션을 생성하는 일회성 SSO 교환값이다.
* 서명 개인키는 Supabase Edge Secret에만 저장하고 저장소·브라우저·NAS에는 기록하지 않는다. NAS에는 공개키만 둔다.
* NAS PHP는 허용된 `/mnt/HDD1/Media/DFBlackbox` 아래 MP4만 읽고 쓰기·삭제·디렉터리 탐색 기능을 제공하지 않는다.
* NAS 계정, 장치 토큰, Supabase secret key와 서명 개인키는 로그·응답·문서에 포함하지 않는다.

## 결과

* 원격 마이그레이션 011을 적용하고 `recording-media` 버전 8과 `media-session` 버전 9를 배포했다.
* NAS1DUAL Apache HTTPS의 `/dfblackbox/`에 공개키 검증 Gateway를 배포했다. `config.php`·`common.php` 직접 요청은 403, 무인증 다운로드는 401, 잘못된 SSO 진입은 포털 오류 복귀로 확인했다.
* Release 빌드는 경고 0개·오류 0개였고 self-contained publish가 성공했다.
* 최신 Release 앱이 NAS의 `새로운 프로젝트.mp4` 117,008,916바이트를 DB 카탈로그에 `ready`로 등록했으며 대용량 파일은 NAS에만 유지된다.
* 웹 JavaScript 구문, HTML ID·selector 계약, diff 공백 검사를 통과했다.
* GitHub Pages 최종 배포가 성공했다. 기존 웹 로그인 세션에서 별도 NAS 계정 입력 없이 `Camera 1`의 `녹화 다운로드 (1)`과 112MB 표시를 확인했다.
* 다운로드한 완성 MP4는 117,008,916바이트로 NAS 원본 크기와 정확히 일치했으며 `.crdownload` 잔여 파일 없이 저장되었다.
