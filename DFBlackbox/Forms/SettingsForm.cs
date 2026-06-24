using DFBlackbox.Core;
using DFBlackbox.Models;
using DFBlackbox.Utils;
using System.Text.Json;

namespace DFBlackbox.Forms;

public sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly AppSettings _workingSettings;
    private readonly Action? _applySettings;
    private readonly bool _fullModeSelected;
    private readonly RadioButton _rdoIpCamera = new() { Text = "IP Camera", AutoSize = true };
    private readonly RadioButton _rdoUsbCamera = new() { Text = "USB Camera", AutoSize = true };
    private readonly ComboBox _cmbCameraList = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _cmbResolution = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _cmbFps = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _txtIpAddress = new();
    private readonly NumericUpDown _numRtspPort = new() { Minimum = 1, Maximum = 65535, Value = 554 };
    private readonly NumericUpDown _numHttpPort = new() { Minimum = 1, Maximum = 65535, Value = 80 };
    private readonly ComboBox _cmbStreamPath = new() { DropDownStyle = ComboBoxStyle.DropDown };
    private readonly CheckBox _chkUseManualRtspUrl = new() { Text = "Use manual RTSP URL", AutoSize = true };
    private readonly TextBox _txtManualRtspUrl = new();
    private readonly TextBox _txtGeneratedRtspUrl = new() { ReadOnly = true };
    private readonly Button _btnRefreshCamera = new() { Text = "Refresh" };
    private readonly NumericUpDown _numRoiDiffThreshold = new() { DecimalPlaces = 3, Increment = 0.005M, Minimum = 0, Maximum = 10 };
    private readonly NumericUpDown _numStopWaitSeconds = new() { Minimum = 1, Maximum = 10 };
    private readonly NumericUpDown _numPreBufferSeconds = new() { Minimum = 0, Maximum = 60 };
    private readonly NumericUpDown _numPreBufferMaxMemory = new() { Minimum = 128, Maximum = 8192, Increment = 128 };
    private readonly NumericUpDown _numDiskStopThreshold = new() { Minimum = 1, Maximum = 100 };
    private readonly NumericUpDown _numDiskResumeThreshold = new() { Minimum = 1, Maximum = 100 };
    private readonly NumericUpDown _numRecRetentionDays = new() { Minimum = 1, Maximum = 3650 };
    private readonly NumericUpDown _numCleanupHour = new() { Minimum = 0, Maximum = 23 };
    private readonly CheckBox _chkCleanupOnStartup = new() { Text = "Cleanup on startup", AutoSize = true };
    private readonly CheckBox _chkStartInTray = new() { Text = "Start in tray", AutoSize = true };
    private readonly CheckBox _chkShowRoi = new() { Text = "ROI / Ignore_ROI", AutoSize = true };
    private readonly CheckBox _chkShowDebugText = new() { Text = "Debug text", AutoSize = true };
    private readonly CheckBox _chkShowPlaybackRoiOutlines = new() { Text = "Playback ROI outlines", AutoSize = true };
    private readonly CheckBox _chkShowPlaybackDiffMessage = new() { Text = "Playback diff message", AutoSize = true };
    private readonly CheckBox _chkShowPlaybackTrackingCandidate = new() { Text = "Playback tracking candidate", AutoSize = true };
    private readonly NumericUpDown _numFullIntervalMinutes = new() { Minimum = 1, Maximum = 1440 };
    private readonly CheckBox _chkAutoStartFullRecording = new() { Text = "Start Full recording on app startup", AutoSize = true };
    private bool _cameraListRefreshInProgress;

    public SettingsForm(AppSettings settings, Action? applySettings = null, bool fullModeSelected = false)
    {
        _settings = settings;
        _workingSettings = CloneSettings(settings);
        _applySettings = applySettings;
        _fullModeSelected = fullModeSelected;
        Text = "DFBlackbox Settings";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Width = 560;
        Height = 820;

        Build();
        LoadSettings();
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

        _cmbResolution.Items.AddRange(new object[] { "640x480", "800x600", "1280x720", "1920x1080" });
        _cmbFps.Items.AddRange(new object[] { "60", "30", "15", "10", "5" });
        _cmbStreamPath.Items.AddRange(new object[]
        {
            "/stream1",
            "/live",
            "/h264",
            "/ch0_0.264",
            "/Streaming/Channels/101",
            "/cam/realmonitor?channel=1&subtype=0"
        });

        panel.Controls.Add(Header("Camera"));
        panel.Controls.Add(Row(_rdoIpCamera, _rdoUsbCamera));
        _btnRefreshCamera.Click += async (_, _) => await RefreshCameraListAsync();
        panel.Controls.Add(Row(Labeled("USB", _cmbCameraList, 300), _btnRefreshCamera));
        panel.Controls.Add(Row(Labeled("Resolution", _cmbResolution, 250), Labeled("FPS", _cmbFps, 130)));
        panel.Controls.Add(Header("IP Camera"));
        panel.Controls.Add(Row(Labeled("IP", _txtIpAddress, 240), Labeled("RTSP", _numRtspPort, 120), Labeled("HTTP", _numHttpPort, 120)));
        panel.Controls.Add(Row(Labeled("Path", _cmbStreamPath, 492)));
        panel.Controls.Add(_chkUseManualRtspUrl);
        panel.Controls.Add(Row(Labeled("Manual RTSP", _txtManualRtspUrl, 492)));
        panel.Controls.Add(Row(Labeled("RTSP URL", _txtGeneratedRtspUrl, 492)));
        panel.Controls.Add(Header("Detection"));
        panel.Controls.Add(Row(Labeled("ROI_Diff", _numRoiDiffThreshold, 240)));
        panel.Controls.Add(Row(Labeled("Recording Stop Wait", _numStopWaitSeconds, 240), Labeled("Pre-record Buffer", _numPreBufferSeconds, 240)));
        panel.Controls.Add(Row(Labeled("Pre-buffer Max MB", _numPreBufferMaxMemory, 240)));
        panel.Controls.Add(Header("Recording"));
        panel.Controls.Add(Row(Labeled("Full Interval Minutes", _numFullIntervalMinutes, 240)));
        panel.Controls.Add(Row(_chkAutoStartFullRecording));
        panel.Controls.Add(Header("Storage"));
        panel.Controls.Add(Row(Labeled("Disk Stop %", _numDiskStopThreshold, 240), Labeled("Disk Resume %", _numDiskResumeThreshold, 240)));
        panel.Controls.Add(Row(Labeled("REC Retention Days", _numRecRetentionDays, 240), Labeled("Cleanup Hour", _numCleanupHour, 240)));
        panel.Controls.Add(Row(_chkCleanupOnStartup, _chkStartInTray));
        panel.Controls.Add(Header("Overlay"));
        panel.Controls.Add(Row(_chkShowRoi, _chkShowDebugText));
        panel.Controls.Add(Row(_chkShowPlaybackRoiOutlines, _chkShowPlaybackDiffMessage));
        panel.Controls.Add(Row(_chkShowPlaybackTrackingCandidate));
        panel.Controls.Add(Buttons());

        foreach (var control in new Control[] { _txtIpAddress, _numRtspPort, _numHttpPort, _cmbStreamPath, _chkUseManualRtspUrl, _txtManualRtspUrl })
        {
            control.TextChanged += (_, _) => UpdateRtspPreview();
        }

        _numRtspPort.ValueChanged += (_, _) => UpdateRtspPreview();
        _chkUseManualRtspUrl.CheckedChanged += (_, _) =>
        {
            UpdateCameraTypeUi();
            UpdateRtspPreview();
        };
        _rdoUsbCamera.CheckedChanged += (_, _) => UpdateCameraTypeUi();
        _rdoIpCamera.CheckedChanged += (_, _) => UpdateCameraTypeUi();
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
            var parts = resolution.Split('x');
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
        var usb = _rdoUsbCamera.Checked;
        _cmbCameraList.Enabled = usb;
        _btnRefreshCamera.Enabled = usb && !_cameraListRefreshInProgress;
        foreach (var control in new Control[] { _txtIpAddress, _numRtspPort, _numHttpPort, _cmbStreamPath, _chkUseManualRtspUrl, _txtGeneratedRtspUrl })
        {
            control.Enabled = !usb;
        }

        _txtManualRtspUrl.Enabled = !usb && _chkUseManualRtspUrl.Checked;
    }

    private Control Buttons()
    {
        var ok = Button("OK", (_, _) =>
        {
            ApplySettings();
            DialogResult = DialogResult.OK;
            Close();
        });
        var apply = Button("Apply", (_, _) => ApplySettings());
        var cancel = Button("Cancel", (_, _) =>
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
        var json = JsonSerializer.Serialize(settings);
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
            Width = 510,
            Height = 34,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 2, 0, 2)
        };
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
}
