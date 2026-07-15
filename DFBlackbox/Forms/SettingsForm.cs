using DFBlackbox.Core;
using DFBlackbox.Models;
using DFBlackbox.Utils;
using Krypton.Toolkit;
using System.Diagnostics;
using System.Text.Json;

namespace DFBlackbox.Forms;

public sealed class SettingsForm : KryptonForm
{
    private const int FormContentWidth = 920;
    private const int WideFieldWidth = 900;
    private const int HalfFieldWidth = 380;
    private const int QuarterFieldWidth = 210;
    private readonly AppSettings _settings;
    private readonly AppSettings _workingSettings;
    private readonly Action? _applySettings;
    private readonly bool _fullModeSelected;
    private readonly bool _recordingOnlyMode;
    private readonly KryptonRadioButton _rdoIpCamera = new() { Text = Localization.T("Main.IpCamera"), AutoSize = true };
    private readonly KryptonRadioButton _rdoUsbCamera = new() { Text = Localization.T("Main.UsbCamera"), AutoSize = true };
    private readonly KryptonComboBox _cmbCameraList = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly KryptonComboBox _cmbResolution = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly KryptonComboBox _cmbFps = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly KryptonTextBox _txtIpAddress = new();
    private readonly KryptonNumericUpDown _numRtspPort = new() { Minimum = 1, Maximum = 65535, Value = 554 };
    private readonly KryptonNumericUpDown _numHttpPort = new() { Minimum = 1, Maximum = 65535, Value = 80 };
    private readonly KryptonComboBox _cmbStreamPath = new() { DropDownStyle = ComboBoxStyle.DropDown };
    private readonly KryptonCheckBox _chkUseManualRtspUrl = new() { Text = Localization.T("Main.ManualRtsp"), AutoSize = true };
    private readonly Button _btnOpenWebsite = new() { Text = Localization.T("Settings.Web"), Width = 100, Height = 28 };
    private readonly KryptonTextBox _txtManualRtspUrl = new();
    private readonly KryptonTextBox _txtGeneratedRtspUrl = new() { ReadOnly = true };
    private readonly Button _btnRefreshCamera = new() { Text = Localization.T("Settings.Refresh") };
    private readonly Button _btnFindOnvifCamera = new() { Text = Localization.T("Settings.FindCamera"), Width = 110, Height = 28 };
    private readonly KryptonComboBox _cmbOnvifCameras = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly KryptonNumericUpDown _numRoiDiffThreshold = new() { DecimalPlaces = 3, Increment = 0.005M, Minimum = 0, Maximum = 10 };
    private readonly KryptonNumericUpDown _numStopWaitSeconds = new() { Minimum = 1, Maximum = 10 };
    private readonly KryptonNumericUpDown _numPreBufferSeconds = new() { Minimum = 0, Maximum = 60 };
    private readonly KryptonNumericUpDown _numPreBufferMaxMemory = new() { Minimum = 128, Maximum = 8192, Increment = 128 };
    private readonly KryptonNumericUpDown _numDiskStopThreshold = new() { Minimum = 1, Maximum = 100 };
    private readonly KryptonNumericUpDown _numDiskResumeThreshold = new() { Minimum = 1, Maximum = 100 };
    private readonly KryptonNumericUpDown _numRecRetentionDays = new() { Minimum = 1, Maximum = 3650 };
    private readonly KryptonNumericUpDown _numCleanupHour = new() { Minimum = 0, Maximum = 23 };
    private readonly KryptonCheckBox _chkCleanupOnStartup = new() { Text = Localization.T("Settings.CleanupOnStartup"), AutoSize = true };
    private readonly KryptonCheckBox _chkStartInTray = new() { Text = Localization.T("Settings.StartInTray"), AutoSize = true };
    private readonly KryptonCheckBox _chkShowRoi = new() { Text = Localization.T("Main.Roi"), AutoSize = true };
    private readonly KryptonCheckBox _chkShowDebugText = new() { Text = Localization.T("Main.DebugText"), AutoSize = true };
    private readonly KryptonCheckBox _chkShowPlaybackRoiOutlines = new() { Text = Localization.T("Settings.PlaybackRoi"), AutoSize = true };
    private readonly KryptonCheckBox _chkShowPlaybackDiffMessage = new() { Text = Localization.T("Settings.PlaybackDiff"), AutoSize = true };
    private readonly KryptonCheckBox _chkShowPlaybackTrackingCandidate = new() { Text = Localization.T("Settings.PlaybackTracking"), AutoSize = true };
    private readonly KryptonRadioButton _rdoPlaybackOptimizeBalanced = new() { Text = Localization.T("Settings.PlaybackOptimizeBalanced"), AutoSize = true };
    private readonly KryptonRadioButton _rdoPlaybackOptimizePlayback = new() { Text = Localization.T("Settings.PlaybackOptimizePlayback"), AutoSize = true };
    private readonly KryptonRadioButton _rdoPlaybackOptimizeTracking = new() { Text = Localization.T("Settings.PlaybackOptimizeTracking"), AutoSize = true };
    private readonly KryptonNumericUpDown _numFullIntervalMinutes = new() { Minimum = 1, Maximum = 1440 };
    private readonly KryptonComboBox _cmbVideoBitrate = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly KryptonCheckBox _chkAutoStartFullRecording = new() { Text = Localization.T("Settings.AutoStartFull"), AutoSize = true };
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
        Text = Localization.T("Settings.Title");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Width = 1000;
        Height = 840;

        Build();
        UiTheme.ApplyFormTheme(this);
        LoadSettings();
        SetOnvifComboMessage(Localization.T("Settings.OnvifInitial"), enabled: true);
        UpdateCameraTypeUi();
        UpdateRtspPreview();
        ApplySettingsVisualTheme();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ApplySettingsVisualTheme();
    }

    private void Build()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(18, 16, 18, 36)
        };
        Controls.Add(panel);

        _cmbResolution.Items.AddRange(new object[] { "320x240", "640x480", "800x600", "1280x720", "1920x1080" });
        _cmbFps.Items.AddRange(new object[] { "60", "30", "15", "10", "5" });
        _cmbVideoBitrate.Items.AddRange(new object[]
        {
            new BitrateOption(Localization.IsEnglish ? "Low (320 kbps)" : "낮음 (320 kbps)", 320),
            new BitrateOption(Localization.IsEnglish ? "Normal (800 kbps)" : "보통 (800 kbps)", 800),
            new BitrateOption(Localization.IsEnglish ? "High (2.5 Mbps)" : "높음 (2.5 Mbps)", 2500)
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

        panel.Controls.Add(Header(Localization.T("Settings.Camera")));
        panel.Controls.Add(Row(_rdoIpCamera, _rdoUsbCamera));
        _btnRefreshCamera.Click += async (_, _) => await RefreshCameraListAsync();
        panel.Controls.Add(Row(Labeled("USB", _cmbCameraList, HalfFieldWidth), _btnRefreshCamera, Labeled(Localization.IsEnglish ? "Resolution" : "해상도", _cmbResolution, 240), Labeled("FPS", _cmbFps, 150)));
        panel.Controls.Add(Header(Localization.T("Settings.IpCamera")));
        panel.Controls.Add(Row(Labeled("IP", _txtIpAddress, HalfFieldWidth), Labeled("RTSP", _numRtspPort, QuarterFieldWidth), Labeled("HTTP", _numHttpPort, QuarterFieldWidth)));
        _btnFindOnvifCamera.Click += async (_, _) => await FindOnvifCameraAsync();
        _cmbOnvifCameras.SelectionChangeCommitted += (_, _) => ApplySelectedOnvifCamera();
        panel.Controls.Add(Row(_btnFindOnvifCamera, _cmbOnvifCameras));
        panel.Controls.Add(Row(Labeled(Localization.T("Settings.Path"), _cmbStreamPath, WideFieldWidth)));
        _btnOpenWebsite.Click += (_, _) => OpenCameraWebsite();
        panel.Controls.Add(Row(_chkUseManualRtspUrl, _btnOpenWebsite));
        panel.Controls.Add(Row(Labeled(Localization.T("Settings.ManualRtsp"), _txtManualRtspUrl, WideFieldWidth)));
        panel.Controls.Add(Row(Labeled(Localization.T("Settings.RtspUrl"), _txtGeneratedRtspUrl, WideFieldWidth)));
        var detectionHeader = Header(Localization.T("Settings.Detection"));
        var detectionRow = Row(Labeled(Localization.T("Settings.RoiDiff"), _numRoiDiffThreshold, 200), Labeled(Localization.T("Settings.StopWait"), _numStopWaitSeconds, 220), Labeled(Localization.T("Settings.PreBuffer"), _numPreBufferSeconds, 220), Labeled(Localization.T("Settings.BufferMax"), _numPreBufferMaxMemory, 230));
        panel.Controls.Add(detectionHeader);
        panel.Controls.Add(detectionRow);
        HideInRecordingOnlyMode(detectionHeader, detectionRow);
        panel.Controls.Add(Header(Localization.T("Settings.Recording")));
        panel.Controls.Add(Row(Labeled(Localization.T("Settings.FullInterval"), _numFullIntervalMinutes, 300), Labeled(Localization.T("Settings.Bitrate"), _cmbVideoBitrate, 300), _chkAutoStartFullRecording));
        panel.Controls.Add(Header(Localization.T("Settings.Storage")));
        panel.Controls.Add(Row(Labeled(Localization.T("Settings.DiskStop"), _numDiskStopThreshold, 210), Labeled(Localization.T("Settings.DiskResume"), _numDiskResumeThreshold, 210), Labeled(Localization.T("Settings.Retention"), _numRecRetentionDays, 230), Labeled(Localization.T("Settings.CleanupHour"), _numCleanupHour, 210)));
        panel.Controls.Add(Row(_chkCleanupOnStartup, _chkStartInTray));
        var overlayHeader = Header(Localization.T("Settings.Overlay"));
        var overlayRow = Row(_chkShowRoi, _chkShowDebugText, _chkShowPlaybackRoiOutlines, _chkShowPlaybackDiffMessage, _chkShowPlaybackTrackingCandidate);
        var playbackOptimizationRow = Row(Labeled(Localization.T("Settings.PlaybackOptimization"), _rdoPlaybackOptimizeBalanced, 220), _rdoPlaybackOptimizePlayback, _rdoPlaybackOptimizeTracking);
        panel.Controls.Add(overlayHeader);
        panel.Controls.Add(overlayRow);
        panel.Controls.Add(playbackOptimizationRow);
        HideInRecordingOnlyMode(overlayHeader, overlayRow, playbackOptimizationRow);
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
        string playbackMode = _workingSettings.Overlay.PlaybackOptimizationMode;
        _rdoPlaybackOptimizePlayback.Checked = string.Equals(playbackMode, "Playback", StringComparison.OrdinalIgnoreCase);
        _rdoPlaybackOptimizeTracking.Checked = string.Equals(playbackMode, "Tracking", StringComparison.OrdinalIgnoreCase);
        _rdoPlaybackOptimizeBalanced.Checked = !_rdoPlaybackOptimizePlayback.Checked && !_rdoPlaybackOptimizeTracking.Checked;
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
        _workingSettings.Overlay.PlaybackOptimizationMode = _rdoPlaybackOptimizePlayback.Checked
            ? "Playback"
            : _rdoPlaybackOptimizeTracking.Checked
                ? "Tracking"
                : "Balanced";
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
        _btnFindOnvifCamera.Text = Localization.T("Settings.Searching");
        SetOnvifComboMessage(Localization.T("Settings.OnvifSearching"), enabled: true);

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
            _cmbOnvifCameras.Items.Insert(0, Localization.T("Settings.OnvifFound", _cmbOnvifCameras.Items.Count));
            _cmbOnvifCameras.SelectedIndex = 0;
            _cmbOnvifCameras.Enabled = true;
        }
        else
        {
            _onvifNoCamerasFound = true;
            SetOnvifComboMessage(Localization.T("Settings.OnvifNone"), enabled: false);
            MessageBox.Show(this, Localization.T("Settings.OnvifNotFound"), "DFBlackbox", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        _onvifDiscoveryInProgress = false;
        _btnFindOnvifCamera.Text = Localization.T("Settings.FindCamera");
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
        ApplySettingsVisualTheme();
    }

    private void ApplySettingsVisualTheme()
    {
        UiTheme.ApplyControlTree(this);
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
            MessageBox.Show(this, Localization.T("Settings.EnterIp"), "DFBlackbox", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string url = $"http://{ipAddress}:{(int)_numHttpPort.Value}";
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }

    private Control Buttons()
    {
        var ok = Button(Localization.T("Button.OK"), (_, _) =>
        {
            ApplySettings();
            DialogResult = DialogResult.OK;
            Close();
        });
        var apply = Button(Localization.T("Button.Apply"), (_, _) => ApplySettings());
        var cancel = Button(Localization.T("Button.Cancel"), (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        });
        ok.Width = 128;
        apply.Width = 128;
        cancel.Width = 128;
        var row = Row(ok, apply, cancel);
        row.Height = 46;
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
        target.Language = source.Language;
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
        var button = new Button { Text = text, Width = 116, Height = 34, Margin = new Padding(0, 4, 10, 4) };
        button.Click += click;
        return button;
    }

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel
        {
            Width = FormContentWidth,
            Height = 42,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 3, 0, 3)
        };
        if (controls.Length == 2 && controls[0] is Button button && controls[1] is KryptonComboBox comboBox)
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
        var panel = new Panel { Width = width, Height = 36, Margin = new Padding(0, 2, 10, 2) };
        control.Dock = DockStyle.Fill;
        panel.Controls.Add(control);
        panel.Controls.Add(new Label
        {
            Text = label,
            Dock = DockStyle.Left,
            Width = Math.Min(160, width / 2),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold)
        });
        return panel;
    }

    private static decimal ClampPort(int port, KryptonNumericUpDown control)
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
