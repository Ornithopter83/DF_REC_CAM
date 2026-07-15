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

- 현재 저장소에서 DB 사용 여부는 확인 필요.
- DB 스키마 변경은 요청 없이는 하지 않는다.

### API 연동

- ONVIF/SOAP 호출이 있다.
- 그 외 외부 API 연동 여부는 확인 필요.
- 인증 정보와 전체 RTSP URL은 로그에 남기지 않는다.

### 배포/운영

- .NET 8 Windows Forms, win-x64, self-contained, single-file publish 구성이 있다.
- `ffmpeg.exe`는 용량이 커서 Git 추적 대상이 아니다.
- 게시 확인 명령은 `dotnet publish DFBlackbox\DFBlackbox.csproj -c Release -o .publishcheck`이다.
