DFBlackbox
==========

Repository
----------

GitHub remote:

- origin: https://github.com/Ornithopter83/DF_REC_CAM.git

Project Overview
----------------

DFBlackbox is a .NET 8 Windows Forms camera recorder built with OpenCvSharp.
It supports IP cameras, USB cameras, live preview, manual recording, auto/event recording, full continuous recording, playback, ROI overlays, and runtime settings.

Source control excludes generated build folders, publish output, Visual Studio local state, runtime logs, settings, baseline images, and recording files through `.gitignore`.

IP Camera LAN Guide
-------------------

Recommended direct-connect setup:

- PC Wi-Fi: internet access, DHCP enabled.
- PC wired LAN: dedicated to the IP camera, no internet gateway.
- IP camera: connected directly to the PC wired LAN port, powered by the DC 12V adapter.

Example wired LAN settings:

- PC wired LAN IP: 192.168.10.10
- PC wired LAN subnet: 255.255.255.0
- PC wired LAN gateway: blank
- PC wired LAN DNS: blank
- Camera IP: 192.168.10.100
- Camera subnet: 255.255.255.0
- Camera gateway: blank

The program does not automatically change Windows network settings or camera network settings.
Set the PC wired LAN IP, camera fixed IP, and camera stream path manually before connecting.

Common RTSP stream path candidates:

- /stream1
- /live
- /h264
- /ch0_0.264
- /Streaming/Channels/101
- /cam/realmonitor?channel=1&subtype=0

Workflow:

1. Start DFBlackbox.
2. Select IP Camera or USB Camera.
3. Enter IP camera address, RTSP port, HTTP port, and stream path.
4. Use Test to verify frame reception.
5. Use Connect to open the camera connection.
6. Use Open Camera to start preview and detection.
7. Use Manual mode for manual-only recording, Auto mode for event-triggered recording, or Full mode for continuous segmented recording.

Camera Startup Behavior
-----------------------

- Manual and Auto modes do not search for cameras on startup.
- Manual and Auto modes begin camera discovery only when Connect or Open Camera is clicked.
- Full mode can automatically search for the camera, connect, open preview, and start recording.
- If no camera is attached, camera-dependent actions remain unavailable until a camera is found or configured.

Overlay Behavior
----------------

- ROI / Ignore ROI controls the live and playback ROI outlines.
- Debug Text controls debug text on the preview/playback image.
- Overlay changes apply immediately after Apply/OK in Settings, and playback redraws the current frame after changes.

Generated Files
---------------

The following are intentionally not tracked by Git:

- `.vs`, `.buildcheck`, `bin`, `obj`, `publish`
- `settings.json`
- `baseline_reference.png`, `home_reference.png`
- `REC`, `Logs`
- temporary files, log files, and active `*.recording.mp4` files
