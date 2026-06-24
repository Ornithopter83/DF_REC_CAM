# DFBlackbox New Thread Handoff

## Project

- Solution/project name: `DFBlackbox`
- Workspace: `C:\Projects\VS\DFBlackbox`
- App type: .NET 8 Windows Forms
- Primary form: `DFBlackbox/Forms/MainForm.cs`

## Current State

- The main window is now an operation-focused screen.
- Camera/detail configuration was moved into `SettingsForm`.
- The right panel keeps the frequently used controls:
  - `Test / Web / Copy`
  - `Connect / Disconnect`
  - `Open Camera / Close Camera`
  - `감시 시작 / 감시 종료`
  - `Manual / Auto`
  - `Start Rec / Stop Rec`
  - `Storage / Settings`
  - `Recent Events`
  - `SAVE / 초기화`

## Important Behavior

- Only one app instance can run.
  - Implemented with `Mutex("DFBlackbox.SingleInstance")` in `Program.cs`.
- Closing the window exits the app.
- Minimizing the window sends it to the tray.
- The tray icon can restore or close the app.
- `Open Camera` is required before `감시` can start.
- `Close Camera` stops watching and stops any active recording.
- Recording files are stored under the executable folder `REC`.
- Logs are stored under the executable folder `Logs`.

## Detection

- Main trigger is `ROI_Diff`.
- Baseline is captured at `감시 시작`.
- `Rod_Area_ROI` exists and is drawn with a thick red overlay.
- If ROI edit targets overlap, `Rod_Area_ROI` has edit priority.
- Weighted detection formula:

```text
ROI_Diff = (ROI changed pixels + Rod_Area_ROI changed pixels * RodAreaWeight) / ROI total pixels
```

- `RodAreaWeight` range: `1~100`
- Default `RodAreaWeight`: `10`
- Recording stop wait is clamped to `1~10` seconds.
  - It writes `RecordingStopWaitSeconds`.
  - The state machine uses that value directly.
- Recording mode is persisted in `RecordingSettings.Mode`.
  - Values: `Manual`, `Auto`, `Full`
  - Default: `Manual`
  - The main radio buttons are restored from this value on launch.
  - Full auto-start requires both `AutoStartFullRecording == true` and `Mode == "Full"`.

## UI Notes

- `SettingsForm` includes:
  - Camera type
  - USB camera index
  - Resolution/FPS
  - IP/RTSP/HTTP/path/manual RTSP
  - Generated RTSP URL
  - `ROI_Diff`
  - `Rod_Area_ROI Weight`
  - `Recording Stop Wait`
  - Overlay options
- `녹화중` is drawn as a blinking red stamp at the top-right of the preview while recording.
- Recording start/end messages are drawn as temporary colored stamps.

## Recent Build

```text
dotnet build DFBlackbox\DFBlackbox.csproj -o .buildcheck
Result: success
Warnings: 0
Errors: 0
```

## Good Next Step

Implement trigger object highlight:

- Use motion contours from `DetectionService`.
- Score boxes by changed pixels, overlap with `ROI`, and overlap with `Rod_Area_ROI`.
- Store top 1~3 trigger boxes in `DetectionResult`.
- Draw blinking boxes for 2~3 seconds after trigger.
