# DFBlackbox Current Work

Updated: 2026-06-24

## Current State

- Project: `C:\Projects\VS\DFBlackbox`
- App: .NET 8 WinForms, OpenCvSharp based blackbox recorder and playback analyzer.
- Settings/log/recording root is the executable folder.
  - Settings: `settings.json`
  - Baseline diff image: `baseline_reference.png`
  - Recordings: `REC`
  - Logs: `Logs`

## Recent Changes

- Git repository was initialized locally and connected to GitHub remote:
  - `origin` -> `https://github.com/Ornithopter83/DF_REC_CAM.git`
- Added `.gitignore` for Visual Studio/.NET build outputs, local IDE state, publish artifacts, runtime settings/logs/recordings, and temporary files.
- Fixed startup/tray behavior so a hidden startup path still shows the tray icon.
- Camera discovery behavior was refined:
  - Full mode auto-start can search/connect/start recording automatically.
  - Manual and Auto modes do not scan cameras on startup.
  - Manual and Auto only start camera discovery after `Connect` or `Open Camera`.
- Fixed playback mode exit so `Connect` and `Open Camera` become available again after loading a video.
- Fixed ROI/debug overlay application so `ROI / Ignore ROI` and `Debug Text` apply immediately with one Apply click.
- Playback overlays now respect the main ROI/debug settings and refresh the current playback frame after overlay changes.
- Fixed broken UI/message text by replacing affected labels/message boxes/tray menu items with ASCII strings.
- Fixed the hidden button to the right of `SAVE` by resizing the bottom `SAVE` / `Defaults` row buttons.
- Recordings are now saved under daily folders:
  - `REC\YYYY\MM\DD\yyyyMMdd_HHmmss.mp4`
  - Active files use the same folder with `.recording.mp4` until finalized.
- Recording writes frames by timestamp at the configured FPS.
  - Excess camera frames are dropped instead of being written into a low-FPS video.
  - This keeps video duration aligned with real elapsed time and reduces file size for long/full-day recording.
- Playback overlays are now controlled from Settings > Overlay:
  - Playback ROI outlines
  - Playback diff message
  - Playback tracking candidate
- Added a `Full` recording mode next to `Manual` and `Auto`.
  - Start Rec with `Full` selected starts continuous recording.
  - Full mode closes the current MP4 and immediately starts a new MP4 every configured interval.
  - The interval is saved in Settings > Recording > Full Interval Minutes.
- When `Full` is selected, the watch button is disabled and watching is forced off.
- `Start Rec` is disabled until `Open Camera` has opened the live preview; recording start handlers also refuse to start without an open preview.
- Settings > Recording now has `Start Full recording on app startup`.
  - The checkbox is enabled only when the main window is currently in `Full` mode.
  - On startup, the app selects Full mode, opens the camera preview, waits up to 20 seconds for a fresh frame, waits 1 more second, then starts Full recording.
  - If tray startup is enabled too, the form stays visible until camera open and Full recording startup have completed, then it hides to tray.
  - Tray startup also schedules this from visible-form paths, `OnHandleCreated`, and a one-shot UI timer fallback.
  - Logs include `Full auto start scheduled.` and `Full auto start running.` for startup diagnosis.
- Watch/playback/default button captions were changed to ASCII text to avoid broken labels in the current build.
- Added `RecordingSettings.FullIntervalMinutes` to settings with a default of 5 minutes.

## Important Behavior

- Manual recording still starts/stops as before.
- Auto recording remains event/detection driven.
- Full recording is independent from the auto-detection state machine and logs each segment as trigger `Full`.
- Crash recovery searches nested recording folders for leftover `.recording.mp4` files.
- Recording output remains clean source frames without overlay graphics burned into MP4 files.
- Playback ROI/boxes are scaled to the MP4 frame ratio.

## Verification

```text
dotnet build DFBlackbox\DFBlackbox.csproj -o .buildcheck -v:q -nologo -clp:ErrorsOnly
Result: success
Warnings: 0
Errors: 0
```

## Next Work

1. Smoke test Full mode with a small interval, confirming sequential MP4 files and event log entries are created.

## 2026-06-23 Update: Persist Manual / Auto / Full Mode

- Added `RecordingSettings.Mode`.
  - Values: `Manual`, `Auto`, `Full`
  - Default: `Manual`
- The main `Manual / Auto / Full` radio selection is now saved when `SAVE` is pressed.
- On the next launch, the saved recording mode is restored into the radio buttons.
- Full auto-start is now gated by both settings:
  - `AutoStartFullRecording == true`
  - `RecordingSettings.Mode == "Full"`
- This prevents an old Full auto-start setting from opening the camera or starting Full recording when the user saved `Manual` or `Auto`.

Verification:

```text
dotnet build DFBlackbox\DFBlackbox.csproj -o .buildcheck
Result: success
Warnings: 0
Errors: 0
```

## 2026-06-24 Update: GitHub / Ignore Rules / Startup Camera Flow

- Initialized the local Git repository because the existing `.git` directory had no Git metadata.
- Connected GitHub remote:
  - `origin`: `https://github.com/Ornithopter83/DF_REC_CAM.git`
- Added `.gitignore` to keep generated files out of source control:
  - `.vs/`, `.buildcheck/`, `bin/`, `obj/`, `publish/`
  - runtime `settings.json`, baseline images, `REC/`, `Logs/`, `*.recording.mp4`
  - local user files and temporary/log files
- Updated camera startup rules:
  - Full mode may auto-discover and auto-connect the camera before starting recording.
  - Manual/Auto do not discover cameras until the user clicks `Connect` or `Open Camera`.
- Fixed overlay Apply behavior:
  - `ROI / Ignore ROI` and `Debug Text` now apply in one click.
  - Playback redraws the current frame after overlay setting changes.

Verification:

```text
dotnet build DFBlackbox\DFBlackbox.csproj -o .buildcheck
Result: success
Warnings: 0
Errors: 0
```
