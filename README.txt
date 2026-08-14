DFBlackbox
==========

개요
----

DFBlackbox는 .NET 8 Windows Forms 기반의 IP/USB 카메라 녹화·감시 애플리케이션입니다. 로컬 녹화와 재생, ROI 기반 감지, ONVIF 탐색, QR 기기 등록, LiveKit 실시간 웹 시청, NAS 전용 녹화 보관과 웹 다운로드를 제공합니다.

현재 운영 원칙
--------------

- 대용량 녹화 파일은 로컬과 NAS에만 저장합니다.
- Supabase에는 조직·장치·카메라·NAS 경로·녹화 카탈로그 메타데이터만 저장합니다.
- 웹 영상은 PC가 온라인일 때 LiveKit 실시간 스트리밍만 재생합니다.
- 녹화본은 웹에서 재생하지 않고 NAS HTTPS Gateway에서 다운로드만 제공합니다.
- 클라우드는 NAS에 직접 접속하지 않으며 NAS 계정 정보를 저장하지 않습니다.

주요 구성 요소
--------------

- `DFBlackbox/`: WinForms 데스크톱 앱
- `supabase/migrations/`: 조직·등록·스트리밍 lease·NAS 카탈로그 DB 마이그레이션 001~015
- `supabase/functions/`: `device-registration`, `media-session`, `recording-media` Edge Function
- `web/`: Supabase 로그인, 기기 승인, LiveKit 실시간 보기, NAS 녹화 다운로드 포털
- `nas-gateway/`: NAS1DUAL Apache/PHP용 SSO, 다운로드, 프로비저닝, 이어올리기 Gateway
- `tasks/`: 번호별 작업서와 검증 결과
- `docs/`: 프로젝트 기준 맥락과 결정 기록

데스크톱 주요 기능
------------------

- IP 카메라 RTSP 및 USB 카메라 연결
- ONVIF 기반 카메라 탐색
- 수동·자동·전체 녹화, 프리버퍼와 분할 녹화
- 로컬 MP4 파일 선택·드래그드롭 재생
- ROI/제외 ROI, 차이 감지와 오버레이
- 한국어/영어 UI와 Krypton Toolkit 테마
- QR 기기 등록, 관리자 승인·등록 해제
- H.264 공유 인코딩 기반 MP4 녹화와 LiveKit RTMPS 송출
- 완성 MP4의 NAS HTTPS 조각 이어올리기와 DB 카탈로그 등록

기기 등록 흐름
--------------

1. 앱에서 `기기 등록`을 시작합니다.
2. QR 또는 승인 페이지를 웹에서 엽니다.
3. Supabase에 로그인하고 조직, 설치 장소, 등록 PC명을 입력해 승인합니다.
4. 앱이 승인 결과와 최초 장치 토큰을 수신합니다.
5. 토큰은 Windows DPAPI로 암호화 저장됩니다.
6. Edge가 발급한 카메라 범위 assertion으로 NAS Gateway가 카메라 폴더를 준비합니다.

신규 카메라 NAS 경로는 `DFBlackbox/{조직명}/{등록 PC명}` 형식입니다. 등록 PC명의 내부 공백은 보존하며 Windows 금지문자는 `_`로 치환합니다. 기존 카메라 경로는 재등록만으로 자동 변경하지 않습니다.

녹화와 NAS 이송
---------------

- 로컬 녹화는 실행 폴더의 `REC/events`, `REC/manual`, `REC/temp`에 저장됩니다.
- 작성 중 파일은 `.recording.mp4`이며 정상 종료 후 최종 `.mp4`가 됩니다.
- `_recording.mp4` 등 이름 확정 전 파일도 전송 대상에서 제외합니다.
- 완성 파일은 안정성 확인과 SHA-256 계산 후 NAS에 4MiB 조각으로 이어올립니다.
- NAS는 offset, 전체 길이와 SHA-256을 검증한 뒤 최종 `recordings/*.mp4`로 전환합니다.
- 네트워크 장애 중에는 로컬 원본을 보존하고 다음 스캔에서 이어서 전송합니다.

웹 포털
--------

- 주소: `https://ornithopter83.github.io/DF_REC_CAM/`
- Supabase Auth 로그인과 조직 RLS를 사용합니다.
- PC가 온라인이면 카메라별 LiveKit 실시간 보기를 시작·중지할 수 있습니다.
- 녹화 목록은 DB 카탈로그에서 읽고 NAS Gateway 다운로드를 새 탭으로 엽니다.
- 루트 재접속과 전체 새로고침에서 NAS SSO 권한을 재교환해 새 카메라 범위를 반영합니다.

설정과 로컬 데이터
------------------

- `settings.json`: 실행 파일 옆의 앱 설정
- `REC/`: 로컬 녹화
- `Logs/`: 앱 로그와 이벤트 JSONL
- `%PROGRAMDATA%\DFBlackboxData`: 카메라 프로필과 기준 데이터 기본 루트
- 장치 토큰: 앱의 DPAPI 토큰 저장소
- 비민감 등록 정보: `HKCU\Software\DFBlackbox\DeviceRegistration`

빌드와 게시
-----------

```powershell
dotnet build DFBlackbox\DFBlackbox.csproj -c Release
dotnet publish DFBlackbox\DFBlackbox.csproj -c Release -o .publishcheck
```

프로젝트는 `net8.0-windows`, `win-x64`, self-contained, single-file로 게시됩니다. 솔루션 루트에 로컬 `ffmpeg.exe`가 있으면 단일 실행 파일에 임베드되며, 용량과 배포 조건 때문에 Git에는 포함하지 않습니다.

개발·운영 문서
--------------

- 현재 상태: `CurrentWork.md`
- 새 스레드 이주: `NewThreadHandoff.md`
- 프로젝트 맥락: `docs/PROJECT_CONTEXT.md`
- 결정 기록: `docs/DECISIONS.md`
- 구성·설정 가이드: `docs/DFBlackbox_프로젝트_구성_및_설정_가이드.docx`
- 세부 작업: `tasks/*.md`

보안 주의
---------

- LiveKit API Secret, Supabase secret key, NAS 계정 정보, RSA 개인키를 저장소나 문서에 기록하지 않습니다.
- 전체 RTSP URL과 인증값을 로그에 남기지 않습니다.
- opaque `sb_secret_` 키는 Edge Function의 관리자 REST RPC에서 `apikey` 헤더로만 사용합니다.
- 실제 운영 비밀값은 Supabase Edge Secrets나 운영 장비의 비추적 설정에만 저장합니다.
