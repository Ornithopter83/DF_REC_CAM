using DFBlackbox.Core;
using DFBlackbox.Models;
using DFBlackbox.Utils;
using System.Diagnostics;
using System.Text.Json;

namespace DFBlackbox.Forms;

public sealed class SettingsForm : Form
{
    private const int FormContentWidth = 800;
    private const int WideFieldWidth = 780;
    private const int HalfFieldWidth = 340;
    private const int QuarterFieldWidth = 190;
    private const string OnvifInitialText = "먼저 카메라 찾기 버튼을 누르세요";
    private const string OnvifNoCameraText = "카메라 없음";
    private const string OnvifSearchingText = "카메라 탐색 중";

    private readonly AppSettings _settings;
    private readonly AppSettings _workingSettings;
    private readonly Action? _applySettings;
    private readonly bool _fullModeSelected;
    private readonly bool _recordingOnlyMode;
    private readonly RadioButton _rdoIpCamera = new() { Text = "IP 카메라", AutoSize = true };
    private readonly RadioButton _rdoUsbCamera = new() { Text = "USB 카메라", AutoSize = true };
    private readonly ComboBox _cmbCameraList = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _cmbResolution = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _cmbFps = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _txtIpAddress = new();
    private readonly NumericUpDown _numRtspPort = new() { Minimum = 1, Maximum = 65535, Value = 554 };
    private readonly NumericUpDown _numHttpPort = new() { Minimum = 1, Maximum = 65535, Value = 80 };
    private readonly ComboBox _cmbStreamPath = new() { DropDownStyle = ComboBoxStyle.DropDown };
    private readonly CheckBox _chkUseManualRtspUrl = new() { Text = "수동 RTSP URL 사용", AutoSize = true };
    private readonly Button _btnOpenWebsite = new() { Text = "웹 설정", Width = 100, Height = 28 };
    private readonly TextBox _txtManualRtspUrl = new();
    private readonly TextBox _txtGeneratedRtspUrl = new() { ReadOnly = true };
    private readonly Button _btnRefreshCamera = new() { Text = "새로고침" };
    private readonly Button _btnFindOnvifCamera = new() { Text = "카메라 찾기", Width = 110, Height = 28 };
    private readonly ComboBox _cmbOnvifCameras = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown _numRoiDiffThreshold = new() { DecimalPlaces = 3, Increment = 0.005M, Minimum = 0, Maximum = 10 };
    private readonly NumericUpDown _numStopWaitSeconds = new() { Minimum = 1, Maximum = 10 };
    private readonly NumericUpDown _numPreBufferSeconds = new() { Minimum = 0, Maximum = 60 };
    private readonly NumericUpDown _numPreBufferMaxMemory = new() { Minimum = 128, Maximum = 8192, Increment = 128 };
    private readonly NumericUpDown _numDiskStopThreshold = new() { Minimum = 1, Maximum = 100 };
    private readonly NumericUpDown _numDiskResumeThreshold = new() { Minimum = 1, Maximum = 100 };
    private readonly NumericUpDown _numRecRetentionDays = new() { Minimum = 1, Maximum = 3650 };
    private readonly NumericUpDown _numCleanupHour = new() { Minimum = 0, Maximum = 23 };
    private readonly CheckBox _chkCleanupOnStartup = new() { Text = "시작 시 정리", AutoSize = true };
    private readonly CheckBox _chkStartInTray = new() { Text = "트레이에서 시작", AutoSize = true };
    private readonly CheckBox _chkShowRoi = new() { Text = "ROI / 제외 ROI", AutoSize = true };
    private readonly CheckBox _chkShowDebugText = new() { Text = "디버그 텍스트", AutoSize = true };
    private readonly CheckBox _chkShowPlaybackRoiOutlines = new() { Text = "재생 ROI 외곽선", AutoSize = true };
    private readonly CheckBox _chkShowPlaybackDiffMessage = new() { Text = "재생 차이 메시지", AutoSize = true };
    private readonly CheckBox _chkShowPlaybackTrackingCandidate = new() { Text = "재생 추적 후보", AutoSize = true };
    private readonly NumericUpDown _numFullIntervalMinutes = new() { Minimum = 1, Maximum = 1440 };
    private readonly ComboBox _cmbVideoBitrate = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _chkAutoStartFullRecording = new() { Text = "앱 시작 시 전체 녹화 시작", AutoSize = true };
    private bool _cameraListRefreshInProgress;
    private bool _onvifDiscoveryInProgress;
    private bool _onvifNoCamerasFound;

    public SettingsForm(AppSettings settings, Action? applySettings = null, bool fullModeSelected = false, bool recordingOnlyMode = false)
    {
        _settings = settings;
        _workingSettings = CloneSettings(settings);
        _applySettings = applySettings;
        _fullModeSelected = fullModeSelected;
        _recordingOnlyMode = recordingOnlyMode;
        Text = "DFBlackbox 설정";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Width = 860;
        Height = 820;

        Build();
        LoadSettings();
        SetOnvifComboMessage(OnvifInitialText, enabled: true);
        UpdateCameraTypeUi();
        UpdateRtspPreview();
    }

    private void Build()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(14, 14, 14, 36)
        };
        Controls.Add(panel);

        _cmbResolution.Items.AddRange(new object[] { "320x240", "640x480", "800x600", "1280x720", "1920x1080" });
        _cmbFps.Items.AddRange(new object[] { "60", "30", "15", "10", "5" });
        _cmbVideoBitrate.Items.AddRange(new object[]
        {
            new BitrateOption("낮음 (320 kbps)", 320),
            new BitrateOption("보통 (800 kbps)", 800),
            new BitrateOption("높음 (2.5 Mbps)", 2500)
        });
        _cmbStreamPath.Items.AddRange(new object[]
        {
            "/stream1",
            "/live",
            "/h264",
            "/ch0_0.264",
            "/Streaming/Channels/101",
            "/cam/realmonitor?channel=1&subtype=0"
        });

        panel.Controls.Add(Header("카메라"));
        panel.Controls.Add(Row(_rdoIpCamera, _rdoUsbCamera));
        _btnRefreshCamera.Click += async (_, _) => await RefreshCameraListAsync();
        panel.Controls.Add(Row(Labeled("USB", _cmbCameraList, HalfFieldWidth), _btnRefreshCamera, Labeled("해상도", _cmbResolution, 220), Labeled("FPS", _cmbFps, 130)));
        panel.Controls.Add(Header("IP 카메라"));
        panel.Controls.Add(Row(Labeled("IP", _txtIpAddress, HalfFieldWidth), Labeled("RTSP", _numRtspPort, 190), Labeled("HTTP", _numHttpPort, 190)));
        _btnFindOnvifCamera.Click += async (_, _) => await FindOnvifCameraAsync();
        _cmbOnvifCameras.SelectionChangeCommitted += (_, _) => ApplySelectedOnvifCamera();
        panel.Controls.Add(Row(_btnFindOnvifCamera, _cmbOnvifCameras));
        panel.Controls.Add(Row(Labeled("경로", _cmbStreamPath, WideFieldWidth)));
        _btnOpenWebsite.Click += (_, _) => OpenCameraWebsite();
        panel.Controls.Add(Row(_chkUseManualRtspUrl, _btnOpenWebsite));
        panel.Controls.Add(Row(Labeled("수동 RTSP", _txtManualRtspUrl, WideFieldWidth)));
        panel.Controls.Add(Row(Labeled("RTSP URL", _txtGeneratedRtspUrl, WideFieldWidth)));
        var detectionHeader = Header("감지");
        var detectionRow = Row(Labeled("ROI 차이", _numRoiDiffThreshold, 160), Labeled("녹화 정지 대기", _numStopWaitSeconds, 205), Labeled("사전 녹화 버퍼", _numPreBufferSeconds, 195), Labeled("버퍼 최대 MB", _numPreBufferMaxMemory, 195));
        panel.Controls.Add(detectionHeader);
        panel.Controls.Add(detectionRow);
        HideInRecordingOnlyMode(detectionHeader, detectionRow);
        panel.Controls.Add(Header("녹화"));
        panel.Controls.Add(Row(Labeled("전체 녹화 간격(분)", _numFullIntervalMinutes, 260), Labeled("녹화 비트 전송률", _cmbVideoBitrate, 260), _chkAutoStartFullRecording));
        panel.Controls.Add(Header("저장소"));
        panel.Controls.Add(Row(Labeled("디스크 정지 %", _numDiskStopThreshold, 175), Labeled("디스크 재개 %", _numDiskResumeThreshold, 175), Labeled("녹화 보관일", _numRecRetentionDays, 210), Labeled("정리 시간", _numCleanupHour, 175)));
        panel.Controls.Add(Row(_chkCleanupOnStartup, _chkStartInTray));
        var overlayHeader = Header("오버레이");
        var overlayRow = Row(_chkShowRoi, _chkShowDebugText, _chkShowPlaybackRoiOutlines, _chkShowPlaybackDiffMessage, _chkShowPlaybackTrackingCandidate);
        panel.Controls.Add(overlayHeader);
        panel.Controls.Add(overlayRow);
        HideInRecordingOnlyMode(overlayHeader, overlayRow);
        panel.Controls.Add(Buttons());

        foreach (var control in new Control[] { _txtIpAddress, _numRtspPort, _numHttpPort, _cmbStreamPath, _chkUseManualRtspUrl, _txtManualRtspUrl })
        {
            control.TextChanged += (_, _) => UpdateRtspPreview();
        }

        _numRtspPort.ValueChanged += (_, _) => UpdateRtspPreview();
        _numHttpPort.ValueChanged += (_, _) => UpdateRtspPreview();
        _chkUseManualRtspUrl.CheckedChanged += (_, _) =>
        {
            UpdateCameraTypeUi();
            UpdateRtspPreview();
        };
        _rdoUsbCamera.CheckedChanged += (_, _) => UpdateCameraTypeUi();
        _rdoIpCamera.CheckedChanged += (_, _) => UpdateCameraTypeUi();
    }

    private void HideInRecordingOnlyMode(params Control[] controls)
    {
        if (!_recordingOnlyMode)
        {
            return;
        }

        foreach (var control in controls)
        {
            control.Visible = false;
        }
    }

    private void LoadSettings()
    {
        _rdoIpCamera.Checked = _workingSettings.Camera.IsIpCamera;
        _rdoUsbCamera.Checked = !_workingSettings.Camera.IsIpCamera;
        _cmbResolution.SelectedItem = $"{_workingSettings.Camera.ActiveWidth}x{_workingSettings.Camera.ActiveHeight}";
        _cmbFps.SelectedItem = _workingSettings.Camera.ActiveFps.ToString();
        _txtIpAddress.Text = _workingSettings.Camera.IpCamera.IpAddress;
        _numRtspPort.Value = _workingSettings.Camera.IpCamera.RtspPort;
        _numHttpPort.Value = _workingSettings.Camera.IpCamera.HttpPort;
        _cmbStreamPath.Text = _workingSettings.Camera.IpCamera.StreamPath;
        _chkUseManualRtspUrl.Checked = _workingSettings.Camera.IpCamera.UseManualRtspUrl;
        _txtManualRtspUrl.Text = _workingSettings.Camera.IpCamera.ManualRtspUrl;
        _numRoiDiffThreshold.Value = (decimal)_workingSettings.Detection.PersonMotionRatioThreshold;
        _numStopWaitSeconds.Value = Math.Clamp(_workingSettings.Detection.RecordingStopWaitSeconds, (int)_numStopWaitSeconds.Minimum, (int)_numStopWaitSeconds.Maximum);
        _numPreBufferSeconds.Value = Math.Clamp(_workingSettings.Detection.PreBufferSeconds, (int)_numPreBufferSeconds.Minimum, (int)_numPreBufferSeconds.Maximum);
        _numPreBufferMaxMemory.Value = Math.Clamp(_workingSettings.Detection.PreBufferMaxMemoryMB, (int)_numPreBufferMaxMemory.Minimum, (int)_numPreBufferMaxMemory.Maximum);
        _numFullIntervalMinutes.Value = Math.Clamp(_workingSettings.Recording.FullIntervalMinutes, (int)_numFullIntervalMinutes.Minimum, (int)_numFullIntervalMinutes.Maximum);
        SelectBitrate(_workingSettings.Recording.VideoBitrateKbps);
        _chkAutoStartFullRecording.Checked = _workingSettings.Recording.AutoStartFullRecording;
        _chkAutoStartFullRecording.Enabled = _fullModeSelected;
        _numDiskStopThreshold.Value = Math.Clamp(_workingSettings.Storage.DiskStopThresholdPercent, (int)_numDiskStopThreshold.Minimum, (int)_numDiskStopThreshold.Maximum);
        _numDiskResumeThreshold.Value = Math.Clamp(_workingSettings.Storage.DiskResumeThresholdPercent, (int)_numDiskResumeThreshold.Minimum, (int)_numDiskResumeThreshold.Maximum);
        _numRecRetentionDays.Value = Math.Clamp(_workingSettings.Storage.RecRetentionDays, (int)_numRecRetentionDays.Minimum, (int)_numRecRetentionDays.Maximum);
        _numCleanupHour.Value = Math.Clamp(_workingSettings.Storage.CleanupHour, (int)_numCleanupHour.Minimum, (int)_numCleanupHour.Maximum);
        _chkCleanupOnStartup.Checked = _workingSettings.Storage.CleanupOnStartup;
        _chkStartInTray.Checked = _workingSettings.Storage.StartInTray;
        _chkShowRoi.Checked = _workingSettings.Overlay.ShowRodRoi;
        _chkShowDebugText.Checked = _workingSettings.Overlay.ShowDebugText;
        _chkShowPlaybackRoiOutlines.Checked = _workingSettings.Overlay.ShowPlaybackRoiOutlines;
        _chkShowPlaybackDiffMessage.Checked = _workingSettings.Overlay.ShowPlaybackDiffMessage;
        _chkShowPlaybackTrackingCandidate.Checked = _workingSettings.Overlay.ShowPlaybackTrackingCandidate;
    }

    private void SaveSettings()
    {
        _workingSettings.Camera.CameraType = _rdoIpCamera.Checked ? "IP" : "USB";
        _workingSettings.Camera.IpCamera.IpAddress = _txtIpAddress.Text.Trim();
        _workingSettings.Camera.IpCamera.RtspPort = (int)_numRtspPort.Value;
        _workingSettings.Camera.IpCamera.HttpPort = (int)_numHttpPort.Value;
        _workingSettings.Camera.IpCamera.Username = "";
        _workingSettings.Camera.IpCamera.Password = "";
        _workingSettings.Camera.IpCamera.StreamPath = _cmbStreamPath.Text.Trim();
        _workingSettings.Camera.IpCamera.UseManualRtspUrl = _chkUseManualRtspUrl.Checked;
        _workingSettings.Camera.IpCamera.ManualRtspUrl = _txtManualRtspUrl.Text.Trim();
        if (_cmbCameraList.SelectedItem is int index)
        {
            _workingSettings.Camera.UsbCamera.DeviceIndex = index;
        }

        if (_cmbResolution.SelectedItem is string resolution)
        {
            string[] parts = resolution.Split('x');
            if (_workingSettings.Camera.IsIpCamera)
            {
                _workingSettings.Camera.IpCamera.Width = int.Parse(parts[0]);
                _workingSettings.Camera.IpCamera.Height = int.Parse(parts[1]);
            }
            else
            {
                _workingSettings.Camera.UsbCamera.Width = int.Parse(parts[0]);
                _workingSettings.Camera.UsbCamera.Height = int.Parse(parts[1]);
            }
        }

        if (_cmbFps.SelectedItem is string fps)
        {
            if (_workingSettings.Camera.IsIpCamera)
            {
                _workingSettings.Camera.IpCamera.Fps = int.Parse(fps);
            }
            else
            {
                _workingSettings.Camera.UsbCamera.Fps = int.Parse(fps);
            }
        }

        _workingSettings.Detection.PersonMotionRatioThreshold = (double)_numRoiDiffThreshold.Value;
        _workingSettings.Detection.RecordingStopWaitSeconds = (int)_numStopWaitSeconds.Value;
        _workingSettings.Detection.PreBufferSeconds = (int)_numPreBufferSeconds.Value;
        _workingSettings.Detection.PreBufferMaxMemoryMB = (int)_numPreBufferMaxMemory.Value;
        _workingSettings.Recording.FullIntervalMinutes = (int)_numFullIntervalMinutes.Value;
        _workingSettings.Recording.VideoBitrateKbps = _cmbVideoBitrate.SelectedItem is BitrateOption bitrate
            ? bitrate.Kbps
            : 800;
        _workingSettings.Recording.AutoStartFullRecording = _chkAutoStartFullRecording.Checked;
        _workingSettings.Storage.DiskStopThresholdPercent = (int)_numDiskStopThreshold.Value;
        _workingSettings.Storage.DiskResumeThresholdPercent = Math.Min((int)_numDiskResumeThreshold.Value, _workingSettings.Storage.DiskStopThresholdPercent);
        _workingSettings.Storage.RecRetentionDays = (int)_numRecRetentionDays.Value;
        _workingSettings.Storage.CleanupHour = (int)_numCleanupHour.Value;
        _workingSettings.Storage.CleanupOnStartup = _chkCleanupOnStartup.Checked;
        _workingSettings.Storage.StartInTray = _chkStartInTray.Checked;
        _workingSettings.Overlay.ShowRodRoi = _chkShowRoi.Checked;
        _workingSettings.Overlay.ShowDebugText = _chkShowDebugText.Checked;
        _workingSettings.Overlay.ShowPlaybackRoiOutlines = _chkShowPlaybackRoiOutlines.Checked;
        _workingSettings.Overlay.ShowPlaybackDiffMessage = _chkShowPlaybackDiffMessage.Checked;
        _workingSettings.Overlay.ShowPlaybackTrackingCandidate = _chkShowPlaybackTrackingCandidate.Checked;
    }

    private async Task RefreshCameraListAsync()
    {
        if (_cameraListRefreshInProgress || IsDisposed)
        {
            return;
        }

        _cameraListRefreshInProgress = true;
        _btnRefreshCamera.Enabled = false;

        List<int> indexes;
        try
        {
            indexes = await Task.Run(() => CameraService.FindCameraIndexes());
        }
        catch
        {
            indexes = [];
        }

        if (IsDisposed)
        {
            return;
        }

        _cmbCameraList.Items.Clear();
        foreach (var index in indexes)
        {
            _cmbCameraList.Items.Add(index);
        }

        if (_cmbCameraList.Items.Count > 0)
        {
            _cmbCameraList.SelectedItem = _cmbCameraList.Items.Contains(_workingSettings.Camera.UsbCamera.DeviceIndex)
                ? _workingSettings.Camera.UsbCamera.DeviceIndex
                : _cmbCameraList.Items[0];
        }
        else
        {
            _cmbCameraList.SelectedItem = null;
            _cmbCameraList.Text = "";
        }

        _cameraListRefreshInProgress = false;
        UpdateCameraTypeUi();
    }

    private async Task FindOnvifCameraAsync()
    {
        if (_onvifDiscoveryInProgress || IsDisposed)
        {
            return;
        }

        _rdoIpCamera.Checked = true;
        _onvifDiscoveryInProgress = true;
        _onvifNoCamerasFound = false;
        _btnFindOnvifCamera.Enabled = false;
        _btnFindOnvifCamera.Text = "찾는 중...";
        SetOnvifComboMessage(OnvifSearchingText, enabled: true);

        List<OnvifCameraDiscoveryResult> cameras;
        try
        {
            var discovery = new OnvifDiscoveryService();
            cameras = await discovery.DiscoverAsync(TimeSpan.FromSeconds(6));
        }
        catch
        {
            cameras = [];
        }

        if (IsDisposed)
        {
            return;
        }

        _cmbOnvifCameras.Items.Clear();
        foreach (var camera in cameras)
        {
            _cmbOnvifCameras.Items.Add(camera);
        }

        if (_cmbOnvifCameras.Items.Count > 0)
        {
            _cmbOnvifCameras.Items.Insert(0, $"{_cmbOnvifCameras.Items.Count} 카메라 탐색");
            _cmbOnvifCameras.SelectedIndex = 0;
            _cmbOnvifCameras.Enabled = true;
        }
        else
        {
            _onvifNoCamerasFound = true;
            SetOnvifComboMessage(OnvifNoCameraText, enabled: false);
            MessageBox.Show(this, "로컬 네트워크에서 ONVIF 카메라를 찾지 못했습니다.", "DFBlackbox", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        _onvifDiscoveryInProgress = false;
        _btnFindOnvifCamera.Text = "카메라 찾기";
        UpdateCameraTypeUi();
    }

    private void ApplySelectedOnvifCamera()
    {
        if (_cmbOnvifCameras.SelectedItem is not OnvifCameraDiscoveryResult camera)
        {
            return;
        }

        _rdoIpCamera.Checked = true;
        _txtIpAddress.Text = camera.IpAddress;
        _numRtspPort.Value = 554;
        _chkUseManualRtspUrl.Checked = false;
        _txtManualRtspUrl.Text = "";
        if (camera.HttpPort is int httpPort)
        {
            _numHttpPort.Value = ClampPort(httpPort, _numHttpPort);
        }

        if (!string.IsNullOrWhiteSpace(camera.RtspUri) && Uri.TryCreate(camera.RtspUri, UriKind.Absolute, out var rtspUri))
        {
            _chkUseManualRtspUrl.Checked = true;
            _txtManualRtspUrl.Text = camera.RtspUri;
            if (rtspUri.Port > 0)
            {
                _numRtspPort.Value = ClampPort(rtspUri.Port, _numRtspPort);
            }

            string path = rtspUri.PathAndQuery;
            if (!string.IsNullOrWhiteSpace(path))
            {
                if (!_cmbStreamPath.Items.Contains(path))
                {
                    _cmbStreamPath.Items.Insert(0, path);
                }

                _cmbStreamPath.Text = path;
            }
        }
        else
        {
            _cmbStreamPath.Text = _cmbStreamPath.Items.Count > 0 ? _cmbStreamPath.Items[0]?.ToString() ?? "" : "";
        }

        if (camera.Width is int width && camera.Height is int height)
        {
            string resolution = $"{width}x{height}";
            if (_cmbResolution.Items.Contains(resolution))
            {
                _cmbResolution.SelectedItem = resolution;
            }
        }

        UpdateCameraTypeUi();
        UpdateRtspPreview();
    }

    private void UpdateRtspPreview()
    {
        var cam = new IpCameraSettings
        {
            IpAddress = _txtIpAddress.Text.Trim(),
            RtspPort = (int)_numRtspPort.Value,
            HttpPort = (int)_numHttpPort.Value,
            Username = "",
            Password = "",
            StreamPath = _cmbStreamPath.Text.Trim(),
            UseManualRtspUrl = _chkUseManualRtspUrl.Checked,
            ManualRtspUrl = _txtManualRtspUrl.Text.Trim()
        };
        _txtGeneratedRtspUrl.Text = RtspUrlBuilder.Build(cam);
    }

    private void UpdateCameraTypeUi()
    {
        bool usb = _rdoUsbCamera.Checked;
        _cmbCameraList.Enabled = usb;
        _btnRefreshCamera.Enabled = usb && !_cameraListRefreshInProgress;
        foreach (var control in new Control[] { _txtIpAddress, _numRtspPort, _numHttpPort, _cmbStreamPath, _chkUseManualRtspUrl, _txtGeneratedRtspUrl, _btnOpenWebsite })
        {
            control.Enabled = !usb;
        }

        _cmbOnvifCameras.Enabled = !usb && !_onvifNoCamerasFound;
        _btnFindOnvifCamera.Enabled = !usb && !_onvifDiscoveryInProgress;
        _txtManualRtspUrl.Enabled = !usb && _chkUseManualRtspUrl.Checked;
    }

    private void SetOnvifComboMessage(string message, bool enabled)
    {
        _cmbOnvifCameras.Items.Clear();
        _cmbOnvifCameras.Items.Add(message);
        _cmbOnvifCameras.SelectedIndex = 0;
        _cmbOnvifCameras.Enabled = enabled;
    }

    private void OpenCameraWebsite()
    {
        string ipAddress = _txtIpAddress.Text.Trim();
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            MessageBox.Show(this, "먼저 IP 카메라 주소를 입력하세요.", "DFBlackbox", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string url = $"http://{ipAddress}:{(int)_numHttpPort.Value}";
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }

    private Control Buttons()
    {
        var ok = Button("확인", (_, _) =>
        {
            ApplySettings();
            DialogResult = DialogResult.OK;
            Close();
        });
        var apply = Button("적용", (_, _) => ApplySettings());
        var cancel = Button("취소", (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        });
        ok.Width = 120;
        apply.Width = 120;
        cancel.Width = 120;
        var row = Row(ok, apply, cancel);
        row.Height = 42;
        row.Margin = new Padding(0, 8, 0, 12);
        return row;
    }

    private void ApplySettings()
    {
        SaveSettings();
        CopySettings(_workingSettings, _settings);
        _applySettings?.Invoke();
    }

    private static AppSettings CloneSettings(AppSettings settings)
    {
        string json = JsonSerializer.Serialize(settings);
        return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
    }

    private static void CopySettings(AppSettings source, AppSettings target)
    {
        target.Camera = CloneSettings(source).Camera;
        target.Detection = CloneSettings(source).Detection;
        target.Rois = CloneSettings(source).Rois;
        target.Overlay = CloneSettings(source).Overlay;
        target.Storage = CloneSettings(source).Storage;
        target.Recording = CloneSettings(source).Recording;
    }

    private static Label Header(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font("Segoe UI", 10F, FontStyle.Bold),
        Margin = new Padding(0, 12, 0, 4)
    };

    private static Button Button(string text, EventHandler click)
    {
        var button = new Button { Text = text, Width = 100, Height = 28, Margin = new Padding(0, 2, 6, 2) };
        button.Click += click;
        return button;
    }

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel
        {
            Width = FormContentWidth,
            Height = 34,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 2, 0, 2)
        };
        if (controls.Length == 2 && controls[0] is Button button && controls[1] is ComboBox comboBox)
        {
            comboBox.Width = row.Width - button.Width - button.Margin.Horizontal - comboBox.Margin.Horizontal - 4;
        }

        foreach (var control in controls)
        {
            row.Controls.Add(control);
        }

        return row;
    }

    private static Panel Labeled(string label, Control control, int width)
    {
        var panel = new Panel { Width = width, Height = 30, Margin = new Padding(0, 0, 6, 0) };
        control.Dock = DockStyle.Fill;
        panel.Controls.Add(control);
        panel.Controls.Add(new Label { Text = label, Dock = DockStyle.Left, Width = Math.Min(150, width / 2), TextAlign = ContentAlignment.MiddleLeft });
        return panel;
    }

    private static decimal ClampPort(int port, NumericUpDown control)
    {
        return Math.Clamp(port, (int)control.Minimum, (int)control.Maximum);
    }

    private void SelectBitrate(int kbps)
    {
        foreach (var item in _cmbVideoBitrate.Items)
        {
            if (item is BitrateOption option && option.Kbps == kbps)
            {
                _cmbVideoBitrate.SelectedItem = item;
                return;
            }
        }

        _cmbVideoBitrate.SelectedIndex = 1;
    }

    private sealed record BitrateOption(string Label, int Kbps)
    {
        public override string ToString() => Label;
    }
}
