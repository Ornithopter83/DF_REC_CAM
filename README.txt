DFBlackbox
==========

저장소
------

- GitHub 원격 저장소: https://github.com/Ornithopter83/DF_REC_CAM.git
- 앱 유형: .NET 8 Windows Forms
- 주요 라이브러리: OpenCvSharp, FFmpeg

프로젝트 개요
-------------

DFBlackbox는 IP 카메라와 USB 카메라를 대상으로 하는 Windows Forms 기반 녹화/감시 도구입니다.
실시간 프리뷰, 수동 녹화, 이벤트 기반 자동 녹화, 전체 연속 녹화, 영상 재생, ROI 표시, ONVIF 카메라 탐색, 녹화 해상도와 비트 전송률 설정을 지원합니다.

주요 기능
---------

- IP 카메라 RTSP 연결과 USB 카메라 선택
- ONVIF 기반 카메라 탐색
- 라이브 프리뷰와 영상 파일 재생
- 수동 / 자동 / 전체 녹화 모드
- ROI / 제외 ROI / 디버그 텍스트 오버레이 설정
- 320x240, 640x480, 1280x720, 1920x1080 녹화 해상도
- 낮음 320 kbps, 보통 800 kbps, 높음 2.5 Mbps 비트 전송률
- F11 전체화면 전환
- 단일 실행 파일 배포용 FFmpeg 임베드

IP 카메라 LAN 권장 구성
----------------------

직결 구성 예시는 다음과 같습니다.

- PC Wi-Fi: 인터넷 사용, DHCP 활성화
- PC 유선 LAN: IP 카메라 전용, 인터넷 게이트웨이 없음
- IP 카메라: PC 유선 LAN 포트에 직접 연결, DC 12V 어댑터로 전원 공급

예시 IP 설정:

- PC 유선 LAN IP: 192.168.10.10
- PC 유선 LAN 서브넷: 255.255.255.0
- PC 유선 LAN 게이트웨이: 비움
- PC 유선 LAN DNS: 비움
- 카메라 IP: 192.168.10.100
- 카메라 서브넷: 255.255.255.0
- 카메라 게이트웨이: 비움

프로그램은 Windows 네트워크 설정이나 카메라 네트워크 설정을 자동으로 변경하지 않습니다.
PC 유선 LAN IP, 카메라 고정 IP, 카메라 스트림 경로는 연결 전에 직접 설정해야 합니다.

자주 쓰는 RTSP 스트림 경로 후보:

- /stream1
- /live
- /h264
- /ch0_0.264
- /Streaming/Channels/101
- /cam/realmonitor?channel=1&subtype=0

기본 사용 흐름
--------------

1. DFBlackbox를 실행합니다.
2. IP 카메라 또는 USB 카메라를 선택합니다.
3. IP 카메라는 주소, RTSP 포트, HTTP 포트, 스트림 경로를 입력합니다.
4. 설정 화면의 `카메라 찾기`로 ONVIF 카메라를 탐색할 수 있습니다.
5. 검색 결과는 자동 반영되지 않고, 콤보박스에서 카메라를 직접 선택했을 때만 설정에 반영됩니다.
6. `연결`로 카메라 연결을 만들고, `카메라 열기`로 프리뷰와 감지를 시작합니다.
7. `수동`, `자동`, `전체` 중 녹화 모드를 선택합니다.

카메라 시작 동작
----------------

- 수동/자동 모드는 앱 시작 시 카메라를 자동 검색하지 않습니다.
- 수동/자동 모드는 `연결` 또는 `카메라 열기`를 눌렀을 때 카메라 검색/연결을 시작합니다.
- 전체 모드는 자동 검색, 연결, 카메라 열기, 녹화 시작 흐름을 사용할 수 있습니다.
- 카메라가 없으면 카메라 의존 기능은 카메라를 찾거나 설정할 때까지 비활성 상태로 남습니다.

오버레이 동작
-------------

- `ROI / 제외 ROI`는 라이브와 재생 화면의 ROI 윤곽선을 제어합니다.
- `디버그 텍스트`는 프리뷰/재생 이미지 위의 상태 텍스트를 제어합니다.
- 모든 오버레이를 끄면 상태창도 표시되지 않습니다.
- 저해상도 카메라 입력에서도 UI 글씨와 선 두께가 과하게 커지지 않도록 1080p 표시 기준의 고정 크기 오버레이를 사용합니다.
- ROI와 감지 박스 좌표는 원본 프레임 좌표와 동기화됩니다.
- 녹화 파일에는 오버레이를 입히지 않은 원본 프레임이 저장됩니다.

전체화면
--------

- F11 키로 전체화면과 창 화면을 전환합니다.
- 설정 창이 열려 있을 때는 F11 전환을 실행하지 않습니다.
- 전체화면에서는 오른쪽 조작 패널, 상태바, 재생 패널을 숨깁니다.
- 전체화면 진입 시 `전체 화면을 종료하려면 F11키를 누르세요` 안내 문구가 20pt로 표시된 뒤 사라집니다.

FFmpeg와 배포
-------------

정확한 비트 전송률 제어를 위해 녹화 저장은 우선 FFmpeg를 사용합니다.

FFmpeg 탐색 순서:

1. 앱 실행 폴더의 `ffmpeg.exe`
2. PATH의 `ffmpeg.exe`
3. 앱에 임베드된 `DFBlackbox.ffmpeg.exe` 리소스

FFmpeg를 찾지 못하면 비트 전송률 지정은 적용하지 않고 OpenCV 기본 녹화기로 MP4 저장을 계속합니다.
솔루션 루트에 `ffmpeg.exe`가 있으면 게시 시 단일 실행 파일 안에 임베드됩니다.
`ffmpeg.exe`는 용량이 커서 Git에는 포함하지 않고, `.gitignore`에서 제외합니다.

로컬 게시 확인 명령:

```text
dotnet publish DFBlackbox\DFBlackbox.csproj -c Release -o .publishcheck
```

개발 참고
---------

- 메인 폼: `DFBlackbox/Forms/MainForm.cs`
- 설정 폼: `DFBlackbox/Forms/SettingsForm.cs`
- 녹화 설정 모델: `DFBlackbox/Models/RecordingSettings.cs`
- FFmpeg 녹화 서비스: `DFBlackbox/Core/RecordingService.cs`
- ONVIF 탐색 서비스: `DFBlackbox/Core/OnvifDiscoveryService.cs`

Git에서 제외하는 파일
---------------------

다음 파일과 폴더는 의도적으로 추적하지 않습니다.

- `.vs`, `.buildcheck`, `.publishcheck`, `bin`, `obj`, `publish`
- `settings.json`
- `baseline_reference.png`, `home_reference.png`
- `REC`, `Logs`
- 임시 파일, 로그 파일, `*.recording.mp4`
- 배포용 로컬 의존성 `ffmpeg.exe`
