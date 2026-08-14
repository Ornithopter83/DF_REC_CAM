# 작업명

공유 H.264 인코딩 기반 NAS 이송 및 웹 미디어 제공

## 배경

기기 등록 후 DFBlackbox가 만드는 NAS 폴더는 현재 쓰기 권한만 검증하며, 실행 폴더의 `REC`에서 완성된 녹화 파일을 NAS로 이송하지 않는다. 또한 실시간 송출을 현재 녹화 writer와 별도로 구현하면 같은 카메라 프레임을 녹화용과 송출용으로 두 번 인코딩하게 된다.

## 확정된 사용자 기능

1. 웹에서 현재 카메라의 실시간 화면을 본다.
2. 웹에서 NAS에 보관된 녹화영상을 선택해 재생한다.
3. 선택한 녹화영상을 다운로드한다.
4. DFBlackbox가 생성했거나 사용자가 등록된 NAS 녹화 폴더에 넣은 지원 파일은 재검사 후 웹 목록에 나타난다.

## 미디어 처리 원칙

```text
카메라 프레임
  → 단일 H.264 인코더
      ├─ 녹화 소비자: MP4 패키징 → 로컬 REC → NAS 이송
      └─ 실시간 소비자: 재인코딩 없이 미디어 서버 인입 → WebRTC SFU → 웹
```

* 녹화와 실시간 송출이 같은 H.264 압축 결과를 공유한다.
* 녹화는 네트워크 상태와 관계없이 가장 높은 우선순위를 유지한다.
* 실시간 소비자가 느리면 오래된 송출 데이터를 버리거나 연결을 재시작하며 녹화 큐를 막지 않는다.
* 작성 중인 MP4를 웹에 직접 제공하지 않는다.
* FFmpeg가 없는 fallback 환경에서는 기존 OpenCV 녹화를 유지하고 실시간 공유 송출은 비활성화한다.

## 녹화 및 NAS 이송

```text
로컬 REC/*.recording.mp4
  → 녹화 정상 종료
  → 로컬 REC/*.mp4
  → NAS recordings/*.uploading 임시 파일
  → 길이 검증
  → NAS recordings/*.mp4 원자적 이름 변경
```

* NAS가 끊겨도 로컬 녹화는 계속한다.
* NAS 복사는 한 작업씩 순차 실행해 녹화 디스크와 LAN 부하를 제한한다.
* 실패 파일은 앱 재시작과 주기적 재검사에서 다시 시도한다.
* NAS 최종 파일이 동일 크기로 이미 존재하면 멱등 성공으로 처리한다.
* NAS 이송 완료 전 로컬 자동 정리가 원본을 삭제하지 않게 한다.
* 로컬 파일은 NAS 이송 직후 삭제하지 않고 기존 보관기간 정책을 적용한다.

권장 경로:

```text
로컬: REC/{yyyy}/{MM}/{dd}/{timestamp}.mp4
NAS:  {nas_root}/{nas_relative_path}/recordings/{yyyy}/{MM}/{dd}/{timestamp}.mp4
```

## NAS 수동 파일 발견

* `FileSystemWatcher` 이벤트는 빠른 반영에 사용한다.
* 네트워크 드라이브 이벤트 누락을 보완하기 위해 주기적으로 전체 목록을 대조한다.
* `.mp4` 파일 크기와 수정시각이 안정된 뒤 메타데이터를 등록한다.
* `.recording`, `.uploading`, 임시 파일과 지원하지 않는 확장자는 목록에서 제외한다.
* NAS 연결이 잠시 끊겼다는 이유로 DB와 웹 제공 사본을 즉시 삭제하지 않는다.

## 웹 제공 경계

* Supabase DB에는 조직·카메라·녹화 메타데이터와 동기화 상태만 저장한다.
* 녹화 원본은 NAS에 유지하며, 원격 웹 제공 방식은 비공개 객체 저장소 동기화 또는 인증된 로컬 미디어 게이트웨이 중 외부 운영 구성을 확인한 뒤 연결한다.
* 재생·다운로드 URL과 WebRTC 참가 토큰은 로그인 및 조직 권한 확인 후 짧은 유효기간으로 발급한다.
* NAS SMB 경로, 장치 토큰, RTSP 자격 증명과 미디어 서버 비밀키는 브라우저에 전달하지 않는다.

## 구현 단계

### 1단계: 로컬 녹화와 NAS 이송 연결

* 완성된 MP4를 비동기 이송 큐에 넣는다.
* 시작 시 미이송 파일을 재검색한다.
* 임시 파일 복사, 길이 검증, 최종 이름 변경과 재시도를 구현한다.
* 기존 녹화·재생과 종료 순서를 보존한다.

### 2단계: 공유 압축 스트림 경계

* FFmpeg rawvideo 입력 뒤 H.264 인코딩을 한 번만 수행한다.
* 압축 출력을 녹화 소비자와 실시간 소비자에게 독립적으로 전달한다.
* 녹화 소비자는 H.264를 재인코딩하지 않고 MP4로 패키징한다.
* 기존 사전 버퍼, 분할 녹화, 고유 파일명과 crash 복구 정책을 유지한다.

### 3단계: 실시간 SFU 연결

* 선택한 SFU의 RTMP/SRT/WHIP 인입 계약을 별도 송출 소비자로 구현한다.
* 시청자가 없어도 항상 송출할지, 요청 시 송출할지 운영 정책을 적용한다.
* 송출 실패와 재접속이 녹화를 중단시키지 않게 한다.

### 4단계: 녹화 인덱싱 및 웹 재생·다운로드

* NAS 파일 재검사와 녹화 메타데이터 upsert를 구현한다.
* RLS, 재생 URL, 다운로드 URL과 웹 목록·플레이어를 구현한다.
* 대용량 파일 전송과 영상 탐색을 실제 브라우저에서 검증한다.

## 외부 승인 지점

다음 변경은 영향과 이유를 보고한 뒤 진행한다.

* WebRTC SFU 서비스 선택과 계정·비용 발생
* Supabase 녹화 메타데이터 DB 마이그레이션 적용
* 비공개 Storage 버킷 생성과 대용량 영상 업로드
* 새 NuGet/npm 패키지 추가
* Edge Function 및 GitHub Pages 실제 배포

## 완료 조건

* 한 카메라 프레임이 녹화와 실시간 송출을 위해 중복 인코딩되지 않는다.
* 네트워크 또는 NAS 장애가 로컬 녹화를 중단시키지 않는다.
* 완성된 로컬 녹화가 등록된 NAS 경로에 재시도 가능한 방식으로 복사된다.
* NAS에 수동 추가한 지원 영상이 주기적 재검사 후 웹 목록에 나타난다.
* 로그인한 권한 보유자만 실시간·재생·다운로드에 접근한다.
* 기존 카메라 연결·녹화·재생 동작에 회귀가 없다.
* Release 빌드와 게시 검증이 성공한다.

## 결과

* 번들 FFmpeg가 H.264, MPEG-TS, `tee`, FIFO, RTMP/SRT/RTP를 지원하고 WHIP 프로토콜은 직접 지원하지 않는 것을 확인했다.
* `NasRecordingTransferService`를 추가해 완성된 로컬 MP4를 단일 백그라운드 작업으로 NAS `recordings/{로컬 상대 경로}`에 복사하도록 연결했다.
* NAS에는 GUID가 포함된 `.uploading` 임시 이름으로 복사하고 원본과 길이가 같을 때만 최종 MP4 이름으로 변경한다. 동일 크기의 최종 파일은 멱등 성공으로 취급하고 충돌·권한·I/O 오류는 원본을 유지한 채 다음 1분 재검사에서 재시도한다.
* 앱 시작 시 기존 로컬 MP4를 전체 재검사하고, 녹화가 정상 종료돼 이벤트 로그가 기록될 때 새 파일을 즉시 큐에 넣으며, 기기 등록 완료 후에도 재검사를 요청한다.
* 등록된 장치에서는 대응하는 NAS 파일의 존재와 길이가 확인되지 않은 로컬 녹화를 보관기간 자동 정리에서 제외하도록 보호했다. 등록되지 않은 기존 설치의 정리 정책은 변경하지 않았다.
* 종료 시 이송 작업과 스캔 타이머를 취소·정리하며 미완료 원본은 다음 실행에서 다시 발견한다.
* 현재 PC의 Debug 설정이 `S:\HDD1\Media`와 `DFBlackbox/MyCompany/Camera1`을 가리키는 것을 확인했다. Debug `REC`에 기존 MP4가 없어 실제 사용자 파일 복사는 수행되지 않았다.
* LiveKit RTMPS Ingress 확정에 따라 raw 카메라 프레임을 FFmpeg `libx264`로 한 번만 인코딩하고 MPEG-TS 출력을 MP4 녹화 sink와 RTMPS sink에 분배하는 공유 파이프라인을 구현했다. 녹화 sink는 손실 없는 우선 큐를, 송출 sink는 녹화를 막지 않는 독립 제한 큐를 사용한다.
* `dotnet build DFBlackbox\DFBlackbox.csproj -c Release --no-restore` 결과 경고 0개, 오류 0개로 완료했다.
* 임시 로컬 REC/NAS 루트에서 이송 서비스를 실행해 원본 유지, 날짜 상대 경로 보존, 원본·대상 길이 일치와 `.uploading` 임시 파일 정리를 확인했다.
* `dotnet publish DFBlackbox\DFBlackbox.csproj -c Release -o .publishcheck --no-restore`가 성공해 self-contained 단일 파일 게시 구성을 확인했다.
* 사용자가 LiveKit Cloud 프로젝트의 URL·API Key·API Secret을 Supabase Secrets에 등록했다. 실제 값은 소스와 문서에 기록하지 않았다.
* `202608120006_camera_stream_sessions.sql`에 카메라별 room, 재사용 가능한 Ingress 정보, 90초 시청 lease, 장치 폴링·송출 상태를 저장하는 service-role 전용 테이블과 RPC를 작성했다. 사용자는 조직 멤버십으로 카메라 권한을 확인하고 장치는 기존 DPAPI 토큰으로 인증한다.
* `media-session` Edge Function을 추가했다. 사용자 JWT로 시청 lease·5분 구독 전용 LiveKit 토큰을 발급하고, 최초 요청에서만 RTMP Ingress를 생성하며, 장치에는 유효한 lease가 있을 때만 RTMPS URL과 stream key를 반환한다.
* 동시 최초 요청은 DB 선점으로 Ingress 중복 생성을 방지하고, 마지막 heartbeat 이후 90초가 지나면 장치 폴링 응답이 `should_stream=false`가 되도록 했다.
* `livekit-server-sdk@2.17.0`을 버전 고정 import하고 Deno 타입 검사를 통과했다. `supabase db push --dry-run`에서는 006 마이그레이션만 원격 적용 대상으로 확인됐다.
* DFBlackbox는 기존 DPAPI 장치 토큰으로 3초마다 `media-session` 명령을 확인하고, 유효한 시청 lease가 있을 때만 RTMPS 송출을 시작한다. 송출 상태를 서버에 보고하며 lease 종료, 카메라 닫기, 네트워크 실패 시 녹화와 분리해 송출만 정리한다.
* 웹 포털에 LiveKit Client `2.21.0`을 버전 고정해 카메라별 실시간 보기·중지와 25초 lease 갱신을 추가했다. 기존 기기 등록 승인 UI는 유지했다.
* `202608120007_recording_catalog.sql`로 조직·장치·카메라 권한이 연결된 녹화 카탈로그와 비공개 `dfblackbox-recordings` Storage 버킷을 추가하고 원격 DB에 적용했다.
* `recording-media` Edge Function을 배포했다. 장치는 DPAPI 토큰 인증 뒤 SHA-256 멱등 키로 6 MiB TUS 이어올리기를 수행하고, 서버가 비공개 Storage 객체의 실제 길이를 확인한 경우에만 `ready`로 전환한다.
* `RecordingCloudSyncService`가 등록된 NAS 카메라의 `recordings` 폴더를 매분 전체 재검사한다. DFBlackbox가 이송한 파일과 사용자가 수동으로 추가한 안정된 MP4 모두 같은 업로드 흐름을 사용하며 재시작 후에도 저장된 TUS 위치에서 이어 올린다.
* 웹 포털에 카메라별 녹화 목록·cursor 추가 조회·5분 signed URL 재생·선택 영상 다운로드를 추가했다. NAS 절대경로와 Storage 내부 경로는 웹 응답에 노출하지 않는다.
* DB 마이그레이션 001~007의 로컬·원격 이력이 일치하고 원격 dry-run이 최신 상태임을 확인했다. `device-registration`, `media-session`, `recording-media`를 원격 재배포했으며, opaque Supabase secret key를 관리자 RPC의 Bearer 값으로 보내지 않도록 수정했다. 세 API 모두 잘못된 장치 토큰을 401로 반환하는 운영 경계 테스트를 통과했다.
* LiveKit Ingress 생성 후 DB 저장 응답이 실패하면 저장 여부를 재확인하고, 저장되지 않은 Ingress는 즉시 삭제해 재시도 시 고아 리소스가 누적되지 않게 했다. DB 인증·권한·누락·충돌 오류도 401/403/404/409로 구분하며 JSON 본문 제한은 `Content-Length` 유무와 무관하게 실제 UTF-8 크기로 검사한다.
* 녹화 SHA-256은 장치 측 멱등 식별자이며 서버 완료 판정은 비공개 Storage 객체의 실제 길이까지 검증한다. 서버가 Storage 객체 내용을 다시 해시하지 않는 현재 신뢰 경계와, 장치/DB 삭제 시 Storage 객체 및 중단 업로드를 정리하는 운영 수명주기 작업이 별도로 필요함을 기록했다.
* 최종 Release 빌드는 경고 0개, 오류 0개였고 self-contained 게시, 웹 JavaScript 구문, HTML selector/ID, diff whitespace 검사를 통과했다. 실제 카메라 영상의 LiveKit 종단 간 송출과 대용량 실제 NAS 파일 업로드는 운영 앱 실행 환경에서 확인해야 한다.
* 2026-08-13 세션 수명주기 보완 후 Release 빌드와 self-contained 단일 파일 게시를 다시 통과했고, 번들 FFmpeg의 `libx264`·RTMP·RTMPS 지원과 웹·Edge·마이그레이션 정적 계약을 확인했다.
* 현재 PC에는 PnP 카메라가 없어 실제 WebRTC 종단 간 송출과 녹화 동시성 검증은 수행하지 못했다. 신규 008을 원격 적용해 마이그레이션 001~008 이력을 일치시키고 `media-session`을 재배포했으며, 장치 명령 조회와 상태 보고 모두 잘못된 장치 토큰에 401을 반환함을 확인했다.
* `스트리밍` 브랜치 푸시 후 GitHub Pages 배포 워크플로가 성공해 웹 포털 변경 배포를 확인했다.
* 2026-08-14 정책 전환으로 대용량 녹화의 Supabase Storage/TUS 경로와 웹 녹화 재생을 폐기했다. 데스크톱은 NAS 파일 메타데이터만 등록하고 웹은 로그인 필요 NAS HTTPS 다운로드만 제공한다.
* 009·010 마이그레이션으로 조직별 NAS 위치·카메라 NAS 루트·녹화 상대 경로를 DB에 유지하며 기존 Storage 객체와 레거시 행은 삭제하지 않았다.
* 2026-08-14 작업 20에서 기존 SMB 이송 구현을 실행 경로에서 제거하고 NAS HTTPS 4MiB 조각 이어올리기로 대체했다. 대용량 바이트는 Supabase를 통과하지 않으며 NAS 확정과 DB `ready` 전에는 로컬 원본을 보존한다.
