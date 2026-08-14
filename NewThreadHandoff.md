# DFBlackbox 새 스레드 이주 요약

아래 내용을 새 스레드의 첫 지시로 사용한다.

---

`C:\Projects\VS\DFBlackbox`에서 `스트리밍` 브랜치 작업을 이어서 진행해.

먼저 다음 파일을 모두 읽어.

- `AGENTS.md`
- `docs/PROJECT_CONTEXT.md`
- `docs/DECISIONS.md`
- `CurrentWork.md`
- `docs/DFBlackbox_프로젝트_구성_및_설정_가이드.docx`
- `tasks/20_NAS_HTTPS_이어올리기.md`
- `tasks/21_신규PC_NAS_HTTPS_프로비저닝.md`
- `tasks/22_등록명_NAS경로_임시파일완료경계.md`
- `tasks/23_신규등록_NAS준비와_다운로드범위갱신.md`

기준 커밋은 새 스레드 시작 시 `git log -1 --oneline`으로 확인하고 `origin/스트리밍`과 비교해. 기존 작업 트리의 사용자 변경을 임의로 덮어쓰지 마.

현재 구현 상태:

- QR 기반 기기 등록, 관리자 승인·거절, 등록 해제와 Windows DPAPI 장치 토큰 저장 완료
- 신규 등록은 NAS SMB 없이 HTTPS Gateway로 폴더를 준비하며 첫 `approved` 상태에서도 준비 세션 발급 가능
- 신규 카메라명과 NAS 폴더명은 등록 PC명을 사용하고 내부 공백 보존
- 카메라 프레임을 한 번 H.264로 인코딩해 MP4 녹화와 LiveKit RTMPS 송출에 공유
- 웹에서 LiveKit 실시간 보기·중지 구현, 녹화본 웹 재생은 제공하지 않음
- 모든 대용량 녹화는 NAS에만 저장하고 Supabase에는 카탈로그 메타데이터만 저장
- 로컬 완성 MP4를 NAS HTTPS 4MiB 조각으로 이어올리며 길이·SHA-256 검증 후 최종 파일로 전환
- `.recording.mp4`, `_recording.mp4` 등 미완성 파일은 NAS 전송 대상에서 제외
- 웹 로그인 권한을 NAS 8시간 HttpOnly 세션으로 교환해 녹화 목록·다운로드 제공
- 웹 루트 재접속과 전체 새로고침에서 NAS 권한 범위를 재발급해 신규 카메라 다운로드 범위 반영
- Supabase 마이그레이션 001~015 원격 적용 완료
- `device-registration`, `media-session`, `recording-media` Edge Function 배포 완료
- NAS Gateway와 GitHub Pages 배포 완료
- Release 빌드 경고 0·오류 0, self-contained 단일 파일 publish 성공

현재 확인된 운영 상태와 주의점:

- 실제 NAS에 신규 카메라 경로와 MP4가 생성되는 것을 확인함
- 기존 `15F4호기` 폴더는 이전 정책으로 생성됐으며 자동 개명하지 않음
- 기존 NAS SSO 세션에서 신규 카메라 다운로드가 `Recording not found`였던 원인은 파일 부재가 아니라 세션 범위 미갱신이었고 웹 새로고침 재발급 로직을 배포함
- 현재 PC에는 실제 카메라가 없어 LiveKit 종단 간 영상 검증과 녹화·송출 동시성 검증은 미완료
- 실제 비밀값은 코드·문서·로그·응답에 기록하지 말 것

다음 우선 작업:

1. 실제 신규 설치에서 등록 승인 직후 NAS 준비가 재시도 없이 성공하는지 확인한다.
2. 등록명 `15F 4호기`가 신규 NAS 폴더에서도 공백을 보존하는지 확인한다.
3. 웹을 새로고침한 뒤 신규 카메라 녹화본 다운로드가 별도 NAS 로그인 없이 성공하는지 확인한다.
4. 실제 카메라가 준비되면 웹 요청 → 명령 폴링 → RTMPS → LiveKit WebRTC 종단 간 흐름을 검증한다.
5. 녹화 중 송출 시작·중지, 네트워크 중단 후 NAS 이어올리기, lease 만료 종료를 검증한다.
6. 기존 `15F4호기`를 `15F 4호기`로 바꿀 경우 NAS·DB·장치 설정을 함께 이전하는 별도 작업서를 먼저 작성한다.

작업 정책:

- 작업서 내용을 A, B, C처럼 세부 작업 단위로 분리한다.
- 한 번에 1개씩 진행하고 `잔여 작업 n개 (A, B, C...)` 형식으로 완료 항목을 소거한다.
- 중간에 새로 발견된 과제는 임의로 포함하지 않고 별도 지시 대상으로 분리한다.
- 작업서가 바뀔 때 Release 빌드와 검증을 한 번 수행한다.
- 외부 DB 스키마 변경, 새 패키지, 비용 설정 변경은 먼저 영향과 이유를 보고한다.
- 클라우드가 NAS에 직접 접속하거나 대용량 파일을 Supabase로 업로드하는 구조로 변경하지 않는다.

---
