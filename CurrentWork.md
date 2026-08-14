# DFBlackbox 현재 작업 상태

업데이트: 2026-08-14

## 기준 정보

- 저장소: `C:\Projects\VS\DFBlackbox`
- 작업 브랜치: `스트리밍`
- 원격: `origin/스트리밍`
- 애플리케이션: .NET 8 Windows Forms, win-x64, self-contained 단일 파일
- 웹 포털: `https://ornithopter83.github.io/DF_REC_CAM/`
- 원격 DB 마이그레이션: 001~015 적용 완료

## 현재 제품 구조

- 데스크톱 앱은 IP/USB 카메라 프리뷰, 수동·자동·전체 녹화, 로컬 재생, ROI/감지, ONVIF 탐색과 기기 등록을 담당한다.
- 녹화와 LiveKit RTMPS 송출은 FFmpeg H.264 인코딩 결과를 공유하며, 송출 장애가 녹화 큐를 막지 않는다.
- 웹 영상 재생은 PC가 온라인일 때의 LiveKit 실시간 화면만 제공한다.
- 녹화 파일은 로컬 완성 후 NAS HTTPS Gateway로 4MiB 조각 이어올리기하며 대용량 파일은 NAS에만 저장한다.
- Supabase DB에는 조직·장치·카메라·NAS 위치·녹화 상대 경로 등 메타데이터만 저장한다.
- 녹화본은 웹에서 재생하지 않고, Supabase 로그인 권한을 NAS의 8시간 HttpOnly 세션으로 교환한 뒤 다운로드만 제공한다.

## 기기 등록

- 앱에서 등록 요청을 만들고 QR 또는 승인 URL을 연다.
- 관리자가 웹 로그인 후 조직·설치 장소·등록 PC명을 입력해 승인한다.
- 앱은 승인 결과를 폴링하고 최초 1회 장치 토큰을 받아 Windows DPAPI로 암호화 보관한다.
- NAS SMB나 ipDISK Drive는 신규 등록의 필수조건이 아니다.
- 승인 직후 장치가 `approved` 상태여도 NAS 준비 세션을 발급할 수 있으며, NAS Gateway가 카메라 루트와 `live`, `recordings`, `events`, `temp`를 생성·쓰기 검사한다.
- 신규 카메라 표시명과 NAS 폴더명은 등록 PC명을 사용하고 내부 공백을 보존한다. 기존 카메라 경로는 재등록만으로 자동 변경하지 않는다.

## 녹화 이송과 카탈로그

- 앱 실행 폴더의 `REC` 아래 완성 MP4를 매분 다시 검색한다.
- `.recording.mp4`, `_recording.mp4`, `.crashed.mp4`, `.uploading.mp4`는 완성본으로 처리하지 않는다.
- 파일 크기·수정시각 안정성을 확인한 뒤 SHA-256을 계산하고 NAS offset부터 이어올린다.
- NAS는 전체 길이와 SHA-256을 확인한 뒤 같은 볼륨의 최종 MP4로 원자적 전환한다.
- NAS 확정 뒤 DB 카탈로그를 `ready`로 등록하며, 그 전에는 로컬 원본을 자동 정리하지 않는다.
- 네트워크 또는 NAS가 끊기면 로컬 파일을 보존하고 다음 실행 또는 재검사에서 재개한다.

## 웹과 실시간 스트리밍

- 포털은 Supabase Auth 로그인과 조직 RLS를 사용한다.
- 카메라별 `실시간 보기`는 90초 서버 lease와 약 25초 heartbeat를 사용한다.
- 앱은 3초마다 명령을 폴링하고 유효 lease가 있을 때만 RTMPS 송출한다.
- 포털 로그인·루트 재접속·전체 새로고침에서 NAS SSO 범위를 다시 교환하여 새 카메라 권한을 반영한다.
- 녹화 목록은 DB 카탈로그를 조회하며 다운로드는 NAS Gateway의 attachment·Range 응답을 사용한다.

## 보안 경계

- LiveKit API Secret, Supabase secret key, NAS 계정 정보와 RSA 개인키는 저장소·문서·로그에 기록하지 않는다.
- opaque `sb_secret_` 키는 Edge Function의 관리자 REST RPC에서 `apikey` 헤더로만 보낸다.
- 장치 토큰은 DB에 해시만 저장하고 로컬에서는 DPAPI로 보호한다.
- NAS에는 assertion 검증용 공개키만 두며 개인키는 Supabase Edge Secret에만 둔다.
- 전체 RTSP URL과 미디어 인증 정보는 공용 로거에서 마스킹한다.

## 배포 상태

- Edge Function: `device-registration`, `media-session`, `recording-media` 배포 완료
- NAS Gateway: `auth.php`, `download.php`, `logout.php`, `provision.php`, `upload.php` 배포 완료
- GitHub Pages: `스트리밍` 브랜치의 `web/` 자동 배포
- 최근 Release 빌드: 경고 0개, 오류 0개
- 최근 publish: `.publishcheck/DFBlackbox.exe` 생성 성공

## 남은 실제 장비 검증

- 실제 카메라의 웹 시청 요청부터 RTMPS·LiveKit WebRTC 재생까지 종단 간 검증
- 녹화 중 송출 시작·중지 시 분할 파일, 프리버퍼와 재생 가능성 검증
- 네트워크 중단 후 NAS 이어올리기와 LiveKit lease 만료 종료 검증
- 신규 설치에서 첫 NAS 준비가 재시도 없이 성공하고 공백 포함 폴더가 생성되는지 확인
- 기존 `15F4호기` 경로를 `15F 4호기`로 바꾸려면 NAS 파일·DB 카탈로그·해당 PC 설정을 함께 이전하는 별도 작업 필요

## 표준 검증 명령

```powershell
dotnet build DFBlackbox\DFBlackbox.csproj -c Release
dotnet publish DFBlackbox\DFBlackbox.csproj -c Release -o .publishcheck
supabase migration list
```
