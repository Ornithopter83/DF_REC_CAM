# DFBlackbox New Thread Handoff

## Project

- Solution/project name: `DFBlackbox`
- Workspace: `C:\Projects\VS\DFBlackbox`
- App type: .NET 8 Windows Forms
- Primary form: `DFBlackbox/Forms/MainForm.cs`
- GitHub remote: `https://github.com/Ornithopter83/DF_REC_CAM.git`
- Current branch: `main`
- Latest pushed commit at handoff: `11e7c42 Improve playback controls and camera mode flow`

## First Task For Next Thread

Implement ONVIF camera discovery and RTSP settings update.

Context:

- The camera's ONVIF access control has been disabled.
- The user wants a `Find Camera` button for cameras on the same network.
- When clicked, the app should discover ONVIF cameras, obtain camera information, and use that information to update the app's RTSP settings.

Expected behavior:

- Add a `Find Camera` button in `SettingsForm`, near the IP camera settings.
- Discover ONVIF cameras on the local network.
- Show or select discovered camera information:
  - IP address
  - HTTP/ONVIF port when available
  - stream/profile information when available
  - RTSP URI when available
- Update existing settings from the selected discovery result:
  - IP address
  - HTTP port
  - RTSP port or manual RTSP URL
  - stream path/manual RTSP field
- Prefer using an actual ONVIF media stream URI if available.
- If ONVIF discovery finds the camera but cannot get a stream URI, fill IP/port and keep the user-editable stream path candidates.
- Keep ID/password out of the UI for now because access control is disabled.

Implementation notes:

- Search for an existing .NET ONVIF library first only if package/network access is acceptable.
- If avoiding new dependencies, implement WS-Discovery probe over UDP multicast:
  - multicast address: `239.255.255.250:3702`
  - probe type usually includes `dn:NetworkVideoTransmitter`
  - parse `XAddrs` from responses to find ONVIF service URLs.
- After discovery, ONVIF `GetProfiles` + `GetStreamUri` can be implemented via SOAP.
- Keep the first version pragmatic:
  - discovery list + apply IP/URL is enough.
  - full ONVIF profile browsing can be added afterward.

## Current State

- The main window is an operation-focused screen.
- Camera/detail configuration lives in `SettingsForm`.
- The main right panel keeps only frequent controls:
  - `Connect / Disconnect`
  - `Open Camera / Close Camera`
  - `Load Video`
  - `Save Diff Base`
  - `Start Watch`
  - `Manual / Auto / Full`
  - `Start Rec / Stop Rec`
  - `Storage / Settings`
  - `Recent Events`
  - `SAVE / Defaults`
- `Test / Web / Copy` were removed from the main operation panel.
- Camera website access now lives in `SettingsForm` as a `Website` button beside `Use manual RTSP URL`.
- IP camera username/password UI was removed again after ONVIF/RTSP access control was disabled.
  - Model fields still exist for compatibility, but app code clears them when settings are saved.

## Important Behavior

- Only one app instance can run.
  - Implemented with `Mutex("DFBlackbox.SingleInstance")` in `Program.cs`.
- Closing the window exits the app.
- Minimizing the window sends it to the tray.
- Tray icon can restore or close the app.
- Manual/Auto modes do not auto-discover cameras on startup.
- Manual/Auto start camera discovery only when `Connect` or `Open Camera` is clicked.
- Full mode can auto-discover/connect/open/start recording.
- `Open Camera` is required before `Start Watch` or recording can start.
- `Save Diff Base` is enabled only after a live camera preview has received a recent frame.
- During video playback, `Connect` and `Open Camera` remain enabled so the user can switch back to camera mode.
- `Close Camera` stops watching and stops active recording.
- Recording files are stored under the configured app data/recording root.
- Logs are stored under the configured logs folder.

## Playback

- Default WinForms `TrackBar` playback UI was replaced with `PlaybackControl`.
- Playback UI includes:
  - rounded progress bar
  - current/total time labels
  - circular previous/play-next buttons
  - shortcut hints inside buttons: `[ - ]`, `[ / ]`, `[ + ]`
- Playback FPS handling:
  - MP4 `mvhd` duration is read directly when possible.
  - Average FPS is calculated as `frame count / duration seconds`.
  - OpenCV `POS_MSEC` and metadata FPS are fallbacks.
- Playback no longer uses WinForms `Timer`.
  - Uses a `Stopwatch`-scheduled async loop.
  - Normal playback reads frames sequentially with `Read()`.
  - Random seek is used only for user seek/step/redraw operations.
  - Playback preview applies immediately on the UI thread.

## Detection

- Main trigger is `ROI_Diff`.
- Baseline is captured with `Save Diff Base`.
- `ROI / Ignore ROI` and `Debug Text` apply correctly with one Settings apply.
- Playback overlays respect main overlay settings and redraw the current frame after changes.
- Recording output remains clean source frames without overlay graphics burned into MP4 files.

## Recent Build

```text
dotnet build DFBlackbox\DFBlackbox.csproj -o .buildcheck
Result: success
Warnings: 0
Errors: 0
```

## Recent Git

```text
git status --short --branch
Result: clean, main tracks origin/main

git log --oneline -2
11e7c42 Improve playback controls and camera mode flow
3059c3d Initial DFBlackbox project import
```
