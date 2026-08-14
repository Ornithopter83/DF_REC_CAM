# 프로젝트 맥락

Codex는 작업 전 이 문서를 먼저 참고한다. 이 문서는 대화 맥락 대신 저장소 안에서 공유되는 프로젝트 기준 정보다.

## 목적

DFBlackbox는 .NET 8 Windows Forms 기반 블랙박스/감시 녹화 애플리케이션이다. 카메라 프리뷰, 영상 감지, 수동/자동/전체 녹화, 녹화 영상 재생, 설정 관리, 배포용 단일 실행 파일 구성을 포함한다.

확실히 알 수 없는 내용은 추측하지 않고 `확인 필요`로 표시한다.

## 주요 관심사

### 장비 연동

- USB 카메라와 IP 카메라 연결을 다룬다.
- ONVIF 기반 카메라 탐색 기능이 있다.
- 실제 현장 장비 모델, 인증 방식, 네트워크 구성은 확인 필요.

### 카메라 또는 영상 처리

- OpenCvSharp를 사용한다.
- ROI 기반 차이 감지와 기준 이미지 비교 로직이 있다.
- 녹화 저장은 FFmpeg를 우선 사용하고, 없으면 OpenCV 기본 녹화기로 fallback한다.
- 재생 시 MP4 duration과 OpenCV 메타데이터를 이용해 FPS를 보정한다.

### 설정 저장/로드

- 앱 설정은 `settings.json`으로 관리되는 것으로 보인다.
- 설정 파일의 구체 스키마와 하위 호환 정책은 확인 필요.
- 기존 설정 형식은 임의로 변경하지 않는다.

### UI

- Windows Forms 기반이며, 외관에는 `Krypton.Toolkit`을 사용한다.
- 전역 팔레트와 공통 색상/상태 스타일은 `DFBlackbox/Utils/UiTheme.cs`에서 관리한다.
- 주요 화면은 `DFBlackbox/Forms/MainForm.cs`와 관련 Designer 파일에 있다.
- 메인 폼과 설정 폼은 `KryptonForm` 기반이다.
- 영상 표시와 재생 처리 구조는 Krypton UI 테마와 분리해 유지한다.
- Designer 파일 수정은 최소화하고, 레이아웃 회귀를 확인한다.

### DB

- Supabase PostgreSQL을 기기 등록, 조직·장치·카메라 권한, 실시간 시청 lease, 녹화 카탈로그에 사용한다.
- `supabase/migrations`의 001~015가 원격 프로젝트에 적용되어 있다.
- 모든 대용량 녹화 파일은 NAS에만 저장하고, DB에는 NAS 식별자·상대 경로·파일명·크기·녹화 시각 등 카탈로그 메타데이터만 저장한다.
- 기존 비공개 `dfblackbox-recordings` Storage와 관련 스키마는 배포된 레거시 상태이며, 새 녹화 파일을 업로드하는 경로로 사용하지 않는다. 기존 객체 삭제는 별도 승인 없이 수행하지 않는다.
- DB 스키마 변경은 요청 없이는 하지 않는다.

### API 연동

- ONVIF/SOAP 호출이 있다.
- Supabase Edge Function `device-registration`, `media-session`, `recording-media`를 사용한다.
- 실시간 영상은 LiveKit Cloud RTMPS Ingress로 송출하고 웹에서 WebRTC로 구독한다.
- 웹 영상 재생은 PC가 온라인일 때의 LiveKit 실시간 스트리밍만 제공한다.
- 녹화본은 웹 내 재생하지 않고, 권한이 확인된 사용자에게 NAS 다운로드 경로만 제공한다. 데스크톱은 Supabase Storage를 거치지 않고 NAS HTTPS Gateway에 완성 MP4를 직접 이어올린 뒤 카탈로그 메타데이터만 DB에 등록한다.
- NAS1dual의 Apache HTTPS에는 `/dfblackbox/` PHP 다운로드 Gateway가 배포되어 있다. 웹 로그인 사용자는 Edge Function이 서명한 단기 증명으로 8시간 HttpOnly NAS 세션을 만들며, Gateway는 권한 범위의 `recordings/*.mp4`에 대해서만 attachment·Range 다운로드를 제공한다.
- NAS 로그인 자격증명은 웹·DB·Edge Function이 보관하지 않는다. NAS에는 공개키만 두고 서명 개인키는 Supabase Edge Secret에만 둔다.
- 인증 정보와 전체 RTSP URL은 로그에 남기지 않는다.
- 완성 MP4 이송은 ipDISK Drive·SMB 대신 내·외부망 공통 NAS HTTPS Gateway를 사용한다. Edge가 장치 토큰과 카메라 소유권을 확인해 범위 제한 assertion을 발급하고, NAS가 조각 offset·전체 길이·SHA-256을 확인한 뒤 최종 파일로 전환한다.
- NAS 또는 외부망이 끊기면 로컬 완성본을 보존하고 다음 실행이나 주기 재검사에서 NAS offset부터 재개한다. NAS 확정과 DB 카탈로그 `ready`가 모두 완료된 파일만 보존기간 자동 정리 대상이 된다.
- 신규 PC의 기기 등록은 로컬 NAS SMB 루트를 요구하지 않는다. 승인 후 장치 토큰으로 Edge에서 카메라 경로 범위 assertion을 발급받아 NAS HTTPS Gateway가 카메라 루트와 `live`, `recordings`, `events`, `temp`를 생성하고 쓰기 가능 여부를 확인한다.
- 신규 등록에서 새로 생성되는 카메라의 표시명과 NAS 폴더 세그먼트는 관리자가 입력한 등록 PC명을 기준으로 하며 내부 공백을 보존한다. 이미 카메라와 녹화 카탈로그가 있는 재등록은 기존 이름과 NAS 경로를 유지한다.

### 배포/운영

- .NET 8 Windows Forms, win-x64, self-contained, single-file publish 구성이 있다.
- GitHub Pages는 `스트리밍` 브랜치의 정적 `web` 포털을 배포한다.
- 포털은 로그인 및 전체 새로고침 때 카메라별 녹화 카탈로그를 한 번 조회하고, 장치 명령 폴링이 기록한 공인 IP·최근 heartbeat로 온라인 상태를 주기 갱신한다.
- `ffmpeg.exe`는 용량이 커서 Git 추적 대상이 아니다.
- 게시 확인 명령은 `dotnet publish DFBlackbox\DFBlackbox.csproj -c Release -o .publishcheck`이다.
