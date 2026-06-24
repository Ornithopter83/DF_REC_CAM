using System.Diagnostics;
using DFBlackbox.Core;
using DFBlackbox.Models;
using DFBlackbox.Utils;
using OpenCvSharp;

namespace DFBlackbox.Forms;

public sealed partial class MainForm : Form
{
    private AppSettings _settings = new();
    private AppPaths _paths = null!;
    private SettingsManager _settingsManager = null!;
    private Logger _logger = null!;
    private readonly CameraService _cameraService = new();
    private DetectionService _detectionService = new();
    private BlackboxStateMachine _stateMachine = null!;
    private RecordingService _recordingService = null!;
    private StorageCleanupService _cleanupService = new();
    private EventLogService _eventLogService = null!;
    private CancellationTokenSource? _captureCts;
    private Task? _captureTask;
    private readonly FpsCounter _fpsCounter = new();
    private int _previewUpdatePending;
    private bool _manualRecordingRequested;
    private bool _fullRecordingRequested;
    private DateTime _nextFullRecordingRotationAt = DateTime.MinValue;
    private Mat? _latestFrame;
    private DateTime _currentRecordingStartedAt;
    private string _currentTriggerReason = "";
    private double _maxMotionScore;
    private double _maxRodMotionScore;
    private double _minHomeDiffScore = double.MaxValue;
    private DateTime _lastFrameAt = DateTime.MinValue;
    private DateTime _lastDiskStatusAt = DateTime.MinValue;
    private string _cachedDiskStatus = "Disk: unknown";
    private int _consecutiveFrameFailures;
    private readonly FpsCounter _algorithmFpsCounter = new();
    private double _lastAlgorithmMs;
    private bool _lastAlgorithmEnabled;
    private bool _isWatching;
    private bool _isEditingRoi;
    private bool _roiSelected;
    private System.Drawing.Point _roiDragStartFramePoint;
    private Rectangle _roiDragStartRect;
    private RoiEditMode _roiEditMode = RoiEditMode.None;
    private RoiEditTarget _roiEditTarget = RoiEditTarget.None;
    private NotifyIcon? _trayIcon;
    private bool _startInTray;
    private bool _initialTrayHideDone;
    private bool _shutdownCompleted;
    private string _recordingStampText = "";
    private Color _recordingStampBackColor = Color.Empty;
    private DateTime _recordingStampUntil = DateTime.MinValue;
    private System.Windows.Forms.Timer? _cleanupTimer;
    private DateTime _lastCleanupDate = DateTime.MinValue;
    private DateTime _lastDiskFullLogAt = DateTime.MinValue;
    private bool _recordingBlockedByDisk;
    private bool _autoStartFullRecordingScheduled;
    private bool _autoStartFullRecordingRunning;
    private System.Windows.Forms.Timer? _autoStartFullRecordingTimer;
    private bool _isPlaybackMode;
    private bool _playbackPlaying;
    private string _playbackPath = "";
    private VideoCapture? _playbackCapture;
    private CancellationTokenSource? _playbackPlayCts;
    private int _playbackFrameIndex;
    private int _playbackFrameCount;
    private double _playbackFps = 30;
    private double _playbackDurationSeconds;
    private bool _updatingPlaybackTimeline;
    private bool _cameraListRefreshInProgress;
    private bool _applyingSettingsToUi;

    private enum RoiEditMode
    {
        None,
        Move,
        Left,
        Right,
        Top,
        Bottom,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    private enum RoiEditTarget
    {
        None,
        MainRoi,
        IgnoreRoi
    }

    public MainForm(bool startInTray = false)
    {
        _startInTray = startInTray;
        InitializeComponent();
        WireEvents();
        try
        {
            InitializeApp();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Startup failed: {ex.Message}", "DFBlackbox", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ScheduleAutoStartFullRecording();
        if (!_startInTray || _initialTrayHideDone)
        {
            return;
        }

        _initialTrayHideDone = true;
        if (ShouldAutoStartFullRecording())
        {
            return;
        }

        WindowState = FormWindowState.Minimized;
        HideToTray();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ScheduleAutoStartFullRecording();
    }

    protected override void SetVisibleCore(bool value)
    {
        if (_startInTray && !_initialTrayHideDone && value)
        {
            if (ShouldAutoStartFullRecording())
            {
                base.SetVisibleCore(value);
                return;
            }

            _initialTrayHideDone = true;
            ScheduleAutoStartFullRecording();
            base.SetVisibleCore(false);
            ShowInTaskbar = false;
            if (_trayIcon is not null)
            {
                _trayIcon.Visible = true;
            }

            return;
        }

        base.SetVisibleCore(value);
    }

    private void WireEvents()
    {
        FormClosing += MainForm_FormClosing;
        Resize += MainForm_Resize;
        KeyDown += MainForm_KeyDown;
        btnRefreshCamera.Click += async (_, _) => await RefreshCameraListAsync();
        btnApplyCamera.Click += (_, _) => SaveSettingsOnly();
        btnWatchToggle.Click += (_, _) => ToggleWatching();
        btnDefaultSettings.Click += (_, _) => ResetDefaults();
        btnConnectCamera.Click += async (_, _) => await ConnectCameraAsync();
        btnDisconnectCamera.Click += async (_, _) => await DisconnectCameraAsync();
        btnOpenCamera.Click += async (_, _) => await OpenCameraPreviewAsync();
        btnCloseCamera.Click += async (_, _) => await CloseCameraPreviewAsync();
        btnLoadVideoFile.Click += async (_, _) => await LoadVideoFileAsync();
        btnCameraProperty.Click += (_, _) => OpenCameraPropertyDialog();
        btnOpenStorageFolder.Click += (_, _) => Process.Start(new ProcessStartInfo { FileName = _paths.RecVideos, UseShellExecute = true });
        btnSettings.Click += (_, _) => OpenSettingsDialog();
        btnStartRecording.Click += async (_, _) => await StartSelectedRecordingAsync();
        btnStopRecording.Click += (_, _) => StopSelectedRecording();
        btnSaveHomeReference.Click += (_, _) => SaveDetectionBaselineReference();
        btnOpenEventList.Click += (_, _) => new EventListForm(_eventLogService).Show(this);
        playbackControl.PreviousClicked += (_, _) => StepPlaybackFrame(-1);
        playbackControl.PlayPauseClicked += (_, _) => TogglePlayback();
        playbackControl.NextClicked += (_, _) => StepPlaybackFrame(1);
        playbackControl.SeekRequested += (_, frameIndex) => SeekPlaybackFrame(frameIndex);
        numPersonThreshold.ValueChanged += (_, _) => ReadDetectionSettingsFromUi();
        picCameraPreview.MouseDown += PicCameraPreview_MouseDown;
        picCameraPreview.MouseMove += PicCameraPreview_MouseMove;
        picCameraPreview.MouseUp += PicCameraPreview_MouseUp;

        foreach (var checkBox in new[] { chkShowRodRoi, chkShowDebugText })
        {
            checkBox.CheckedChanged += (_, _) =>
            {
                if (_applyingSettingsToUi)
                {
                    return;
                }

                SyncOverlaySettings();
                RedrawPlaybackFrameIfActive();
            };
        }

        foreach (var control in new Control[] { txtIpAddress, numRtspPort, cmbStreamPath, chkUseManualRtspUrl, txtManualRtspUrl })
        {
            control.TextChanged += (_, _) => UpdateRtspPreview();
        }

        numRtspPort.ValueChanged += (_, _) => UpdateRtspPreview();
        chkUseManualRtspUrl.CheckedChanged += (_, _) =>
        {
            txtManualRtspUrl.Enabled = chkUseManualRtspUrl.Checked;
            UpdateRtspPreview();
        };
        rdoUsbCamera.CheckedChanged += (_, _) => UpdateCameraTypeUi();
        rdoIpCamera.CheckedChanged += (_, _) => UpdateCameraTypeUi();
        rdoManualRecording.CheckedChanged += (_, _) => SaveRecordingModeFromUi();
        rdoAutoRecording.CheckedChanged += (_, _) => SaveRecordingModeFromUi();
        rdoFullRecording.CheckedChanged += (_, _) =>
        {
            if (rdoFullRecording.Checked)
            {
                SetWatching(false);
                SaveRecordingModeFromUi();
                ScheduleAutoStartFullRecording();
            }
            else
            {
                SaveRecordingModeFromUi();
                _autoStartFullRecordingScheduled = false;
            }

            UpdateControlStates();
        };
    }

    private void InitializeApp()
    {
        InitializeTrayIcon();
        _paths = new AppPaths(_settings.Storage);
        _paths.Ensure();
        var legacySettingsRoot = _paths.Root;
        _settingsManager = new SettingsManager(AppContext.BaseDirectory, legacySettingsRoot);
        _settings = _settingsManager.Load();
        _startInTray |= _settings.Storage.StartInTray;
        _paths = new AppPaths(_settings.Storage);
        _paths.Ensure();
        _settingsManager = new SettingsManager(AppContext.BaseDirectory, legacySettingsRoot);
        _logger = new Logger(_paths.Logs);
        _eventLogService = new EventLogService(_paths.EventLogPath);
        _stateMachine = new BlackboxStateMachine(_settings);
        _stateMachine.AutoRecordingEnabled = _isWatching && rdoAutoRecording.Checked;
        _recordingService = new RecordingService(_settings, _paths);
        _recordingService.RecoverCrashedRecordings();
        StartCleanupSchedule();
        _detectionService.LoadBaselineReference(_paths.BaselineReferencePath);
        _detectionService.LoadHomeReference(_paths.HomeReferencePath);
        ApplySettingsToUi();
        _cleanupService.Cleanup(_settings.Storage);
        lblCameraStatus.Text = "Camera: ready";
        lblRtspStatus.Text = _settings.Camera.IsIpCamera ? "RTSP: not connected" : "RTSP: n/a";
        lblLastFrame.Text = "Last Frame: n/a";
        UpdateControlStates();
        ScheduleAutoStartFullRecording();
    }

    private void InitializeTrayIcon()
    {
        _trayIcon?.Dispose();
        _trayIcon = new NotifyIcon
        {
            Icon = Icon,
            Text = "DFBlackbox",
            Visible = false,
            ContextMenuStrip = new ContextMenuStrip()
        };
        _trayIcon.ContextMenuStrip.Items.Add("Open", null, (_, _) => RestoreFromTray());
        _trayIcon.ContextMenuStrip.Items.Add("Exit", null, (_, _) => Close());
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    private void ApplySettingsToUi()
    {
        _applyingSettingsToUi = true;
        try
        {
            rdoIpCamera.Checked = _settings.Camera.IsIpCamera;
            rdoUsbCamera.Checked = !_settings.Camera.IsIpCamera;
            cmbResolution.SelectedItem = $"{_settings.Camera.ActiveWidth}x{_settings.Camera.ActiveHeight}";
            cmbFps.SelectedItem = _settings.Camera.ActiveFps.ToString();
            txtIpAddress.Text = _settings.Camera.IpCamera.IpAddress;
            numRtspPort.Value = _settings.Camera.IpCamera.RtspPort;
            numHttpPort.Value = _settings.Camera.IpCamera.HttpPort;
            cmbStreamPath.Text = _settings.Camera.IpCamera.StreamPath;
            chkUseManualRtspUrl.Checked = _settings.Camera.IpCamera.UseManualRtspUrl;
            txtManualRtspUrl.Text = _settings.Camera.IpCamera.ManualRtspUrl;
            txtManualRtspUrl.Enabled = chkUseManualRtspUrl.Checked;
            chkShowPersonBox.Checked = false;
            chkShowMotionMask.Checked = false;
            chkShowRodRoi.Checked = _settings.Overlay.ShowRodRoi;
            chkShowHomeRoi.Checked = false;
            chkShowDebugText.Checked = _settings.Overlay.ShowDebugText;
            chkShowRecordingStatus.Checked = false;
            chkShowFrameTime.Checked = false;
            numPersonThreshold.Value = (decimal)_settings.Detection.PersonMotionRatioThreshold;
            ApplyRecordingModeToUi();
            UpdateRtspPreview();
            UpdateCameraTypeUi();
        }
        finally
        {
            _applyingSettingsToUi = false;
        }
    }

    private void ApplyRecordingModeToUi()
    {
        var mode = _settings.Recording.Mode;
        if (string.Equals(mode, "Full", StringComparison.OrdinalIgnoreCase))
        {
            rdoFullRecording.Checked = true;
        }
        else if (string.Equals(mode, "Auto", StringComparison.OrdinalIgnoreCase))
        {
            rdoAutoRecording.Checked = true;
        }
        else
        {
            rdoManualRecording.Checked = true;
        }
    }

    private async Task RefreshCameraListAsync()
    {
        if (_cameraListRefreshInProgress || IsDisposed)
        {
            return;
        }

        _cameraListRefreshInProgress = true;
        btnRefreshCamera.Enabled = false;
        if (rdoUsbCamera.Checked)
        {
            lblCameraStatus.Text = "Camera: scanning USB";
        }

        List<int> indexes;
        try
        {
            indexes = await Task.Run(() => CameraService.FindCameraIndexes());
        }
        catch (Exception ex)
        {
            indexes = [];
            _logger?.Error(ex, "USB camera scan failed");
            lblErrorStatus.Text = $"Error: USB camera scan failed ({ex.Message})";
        }

        if (IsDisposed)
        {
            return;
        }

        cmbCameraList.Items.Clear();
        foreach (var index in indexes)
        {
            cmbCameraList.Items.Add(index);
        }

        if (cmbCameraList.Items.Count > 0)
        {
            cmbCameraList.SelectedItem = cmbCameraList.Items.Contains(_settings.Camera.UsbCamera.DeviceIndex)
                ? _settings.Camera.UsbCamera.DeviceIndex
                : cmbCameraList.Items[0];
        }
        else
        {
            cmbCameraList.SelectedItem = null;
            cmbCameraList.Text = "";
            if (rdoUsbCamera.Checked)
            {
                lblCameraStatus.Text = "Camera: no USB camera found";
            }
        }

        _cameraListRefreshInProgress = false;
        UpdateCameraTypeUi();
        UpdateControlStates();
    }

    private void SaveSettingsOnly()
    {
        try
        {
            ReadCameraSettingsFromUi();
            ReadDetectionSettingsFromUi();
            ReadRecordingSettingsFromUi();
            _settingsManager.Save(_settings);
            lblCameraStatus.Text = "Settings: saved";
            MessageBox.Show(this, "Settings saved.", "DFBlackbox", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Saving settings failed");
            lblErrorStatus.Text = $"Error: {ex.Message}";
            MessageBox.Show(this, $"Saving settings failed.\n\n{ex.Message}", "DFBlackbox", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ResetDefaults()
    {
        if (_recordingService?.IsRecording == true)
        {
            MessageBox.Show(this, "Stop recording before resetting defaults.", "DFBlackbox", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _settings = new AppSettings();
        _stateMachine = new BlackboxStateMachine(_settings) { AutoRecordingEnabled = false };
        _detectionService.ResetBackground();
        _detectionService.LoadBaselineReference(_paths.BaselineReferencePath);
        ApplySettingsToUi();
        _settingsManager.Save(_settings);
        lblCameraStatus.Text = "Defaults: restored";
        UpdateControlStates();
    }

    private void ToggleWatching()
    {
        if (rdoFullRecording.Checked)
        {
            SetWatching(false);
            return;
        }

        SetWatching(!_isWatching);
    }

    private void SetWatching(bool watching)
    {
        _isWatching = watching && !rdoFullRecording.Checked && IsCameraPreviewOpen();
        if (_isWatching)
        {
            lblDetectionStatus.Text = File.Exists(_paths.BaselineReferencePath) ? "Detection: watching" : "Detection: baseline missing";
        }
        else
        {
            lblDetectionStatus.Text = "Detection: manual";
        }

        btnWatchToggle.Text = _isWatching ? "Stop Watch" : "Start Watch";
        _stateMachine.AutoRecordingEnabled = _isWatching && rdoAutoRecording.Checked;
        UpdateControlStates();
    }

    private async Task ConnectCameraAsync()
    {
        ExitPlaybackMode(clearPreview: true);
        if (_recordingService?.IsRecording == true)
        {
            MessageBox.Show(this, "Recording is active. Stop recording before changing camera connection.", "DFBlackbox", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        await StopCaptureLoopAsync();
        ReadCameraSettingsFromUi();
        if (!_settings.Camera.IsIpCamera && !await EnsureUsbCameraSelectedAsync())
        {
            lblCameraStatus.Text = "Camera: no USB camera found";
            UpdateControlStates();
            return;
        }

        ReadDetectionSettingsFromUi();
        _settingsManager.Save(_settings);
        btnConnectCamera.Enabled = false;
        lblCameraStatus.Text = "Camera: connecting";
        lblRtspStatus.Text = _settings.Camera.IsIpCamera ? "RTSP: Connecting" : "RTSP: n/a";
        var opened = await Task.Run(() => _cameraService.Open(_settings));
        btnConnectCamera.Enabled = true;
        _lastFrameAt = DateTime.Now;
        _consecutiveFrameFailures = 0;
        lblCameraStatus.Text = opened ? GetCameraStatusText() : GetCameraErrorText();
        lblRtspStatus.Text = _settings.Camera.IsIpCamera ? $"RTSP: {_cameraService.ConnectionState}" : "RTSP: n/a";
        UpdateControlStates();
    }

    private async Task DisconnectCameraAsync()
    {
        await CloseCameraPreviewAsync();
        StopRecordingIfActive(DateTime.Now);
        _cameraService.Close();
        SetWatching(false);
        lblCameraStatus.Text = "Camera: disconnected";
        lblRtspStatus.Text = _settings.Camera.IsIpCamera ? "RTSP: Disconnected" : "RTSP: n/a";
        lblLastFrame.Text = "Last Frame: n/a";
        UpdateControlStates();
    }

    private async Task OpenCameraPreviewAsync()
    {
        ExitPlaybackMode(clearPreview: true);
        if (!_cameraService.IsOpened)
        {
            await ConnectCameraAsync();
        }

        if (!_cameraService.IsOpened || _captureCts is not null)
        {
            return;
        }

        _captureCts = new CancellationTokenSource();
        _captureTask = Task.Run(() => CaptureLoopAsync(_captureCts.Token));
        lblCameraStatus.Text = $"{GetCameraStatusText()} / open";
        UpdateControlStates();
    }

    private async Task CloseCameraPreviewAsync()
    {
        await StopCaptureLoopAsync();
        StopRecordingIfActive(DateTime.Now);
        SetWatching(false);
        var old = picCameraPreview.Image;
        picCameraPreview.Image = null;
        old?.Dispose();
        lblCameraStatus.Text = _cameraService.IsOpened ? $"{GetCameraStatusText()} / closed" : "Camera: closed";
        UpdateControlStates();
    }

    private async Task LoadVideoFileAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Load recording video",
            Filter = "Video files|*.mp4;*.avi;*.mov;*.mkv;*.wmv|All files|*.*",
            InitialDirectory = Directory.Exists(_paths.RecVideos) ? _paths.RecVideos : AppContext.BaseDirectory
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        await EnterPlaybackModeAsync(dialog.FileName);
    }

    private async Task EnterPlaybackModeAsync(string filePath)
    {
        await DisconnectCameraAsync();
        ExitPlaybackMode(clearPreview: false);

        var capture = new VideoCapture(filePath);
        if (!capture.IsOpened())
        {
            capture.Dispose();
            MessageBox.Show(this, "Could not open the selected video file.", "DFBlackbox", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _playbackCapture = capture;
        _playbackPath = filePath;
        _playbackFrameIndex = 0;
        _playbackFrameCount = Math.Max(0, (int)Math.Round(capture.Get(VideoCaptureProperties.FrameCount)));
        _playbackDurationSeconds = TryReadMp4DurationSeconds(filePath);
        _playbackFps = DetectPlaybackFps(capture, filePath, _playbackFrameCount);

        _isPlaybackMode = true;
        _playbackPlaying = false;
        ConfigurePlaybackTimeline();
        playbackPanel.Visible = true;
        playbackPanel.BringToFront();
        playbackControl.IsPlaying = false;
        playbackControl.Value = 0;
        lblCameraStatus.Text = "Playback: loaded";
        lblRtspStatus.Text = $"File: {Path.GetFileName(filePath)}";
        ShowPlaybackFrame(0);
        UpdateControlStates();
    }

    private void ExitPlaybackMode(bool clearPreview)
    {
        StopPlaybackLoop();
        _playbackCapture?.Dispose();
        _playbackCapture = null;
        _isPlaybackMode = false;
        _playbackPlaying = false;
        _playbackPath = "";
        _playbackFrameIndex = 0;
        _playbackFrameCount = 0;
        _playbackDurationSeconds = 0;
        playbackPanel.Visible = false;
        playbackControl.IsPlaying = false;
        if (clearPreview)
        {
            var old = picCameraPreview.Image;
            picCameraPreview.Image = null;
            old?.Dispose();
        }

        UpdateControlStates();
    }

    private double DetectPlaybackFps(VideoCapture capture, string filePath, int frameCount)
    {
        var metadataFps = capture.Get(VideoCaptureProperties.Fps);
        var mp4DurationSeconds = _playbackDurationSeconds > 0 ? _playbackDurationSeconds : TryReadMp4DurationSeconds(filePath);
        var durationFps = frameCount > 1 && mp4DurationSeconds > 0
            ? frameCount / mp4DurationSeconds
            : TryEstimatePlaybackFpsFromOpenCvTimestamp(capture, frameCount);
        if (IsValidPlaybackFps(durationFps))
        {
            if (!IsValidPlaybackFps(metadataFps)
                || Math.Abs(durationFps - metadataFps) / Math.Max(durationFps, metadataFps) > 0.10)
            {
                _logger?.Info($"Playback FPS adjusted from metadata {metadataFps:0.###} to duration estimate {durationFps:0.###}.");
                return durationFps;
            }
        }

        if (IsValidPlaybackFps(metadataFps))
        {
            return metadataFps;
        }

        return Math.Max(1, _settings.Camera.ActiveFps);
    }

    private static double TryEstimatePlaybackFpsFromOpenCvTimestamp(VideoCapture capture, int frameCount)
    {
        if (frameCount <= 1)
        {
            return 0;
        }

        var originalFrame = capture.Get(VideoCaptureProperties.PosFrames);
        try
        {
            capture.Set(VideoCaptureProperties.PosFrames, frameCount - 1);
            using var frame = new Mat();
            if (!capture.Read(frame) || frame.Empty())
            {
                return 0;
            }

            var lastFrameMs = capture.Get(VideoCaptureProperties.PosMsec);
            if (lastFrameMs <= 0 || double.IsNaN(lastFrameMs) || double.IsInfinity(lastFrameMs))
            {
                return 0;
            }

            return (frameCount - 1) / (lastFrameMs / 1000.0);
        }
        finally
        {
            capture.Set(VideoCaptureProperties.PosFrames, Math.Max(0, originalFrame));
        }
    }

    private static double TryReadMp4DurationSeconds(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            return ReadMp4DurationFromContainer(stream, stream.Length);
        }
        catch
        {
            return 0;
        }
    }

    private static double ReadMp4DurationFromContainer(Stream stream, long endOffset)
    {
        while (stream.Position + 8 <= endOffset)
        {
            var atomStart = stream.Position;
            var atomSize = ReadUInt32BigEndian(stream);
            var atomType = ReadAscii(stream, 4);
            long headerSize = 8;
            if (atomSize == 1)
            {
                if (stream.Position + 8 > endOffset)
                {
                    return 0;
                }

                atomSize = 0;
                var extendedSize = ReadUInt64BigEndian(stream);
                headerSize = 16;
                if (extendedSize > long.MaxValue)
                {
                    return 0;
                }

                atomSize = (uint)Math.Min(uint.MaxValue, extendedSize);
                var atomEnd = atomStart + (long)extendedSize;
                var duration = ReadMp4AtomPayload(stream, atomType, atomEnd);
                if (duration > 0)
                {
                    return duration;
                }

                stream.Position = Math.Min(atomEnd, endOffset);
                continue;
            }

            if (atomSize != 0 && atomSize < headerSize)
            {
                return 0;
            }

            var currentAtomEnd = atomSize == 0 ? endOffset : Math.Min(atomStart + atomSize, endOffset);
            var found = ReadMp4AtomPayload(stream, atomType, currentAtomEnd);
            if (found > 0)
            {
                return found;
            }

            stream.Position = currentAtomEnd;
        }

        return 0;
    }

    private static double ReadMp4AtomPayload(Stream stream, string atomType, long atomEnd)
    {
        if (atomType == "mvhd")
        {
            return ReadMovieHeaderDuration(stream, atomEnd);
        }

        if (atomType is "moov" or "trak" or "mdia")
        {
            return ReadMp4DurationFromContainer(stream, atomEnd);
        }

        return 0;
    }

    private static double ReadMovieHeaderDuration(Stream stream, long atomEnd)
    {
        if (stream.Position + 4 > atomEnd)
        {
            return 0;
        }

        var version = stream.ReadByte();
        stream.Position += 3;
        if (version == 1)
        {
            if (stream.Position + 28 > atomEnd)
            {
                return 0;
            }

            stream.Position += 16;
            var timescale = ReadUInt32BigEndian(stream);
            var duration = ReadUInt64BigEndian(stream);
            return timescale > 0 ? duration / (double)timescale : 0;
        }

        if (stream.Position + 16 > atomEnd)
        {
            return 0;
        }

        stream.Position += 8;
        var scale = ReadUInt32BigEndian(stream);
        var duration32 = ReadUInt32BigEndian(stream);
        return scale > 0 ? duration32 / (double)scale : 0;
    }

    private static uint ReadUInt32BigEndian(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[4];
        if (stream.Read(buffer) != buffer.Length)
        {
            return 0;
        }

        return ((uint)buffer[0] << 24)
            | ((uint)buffer[1] << 16)
            | ((uint)buffer[2] << 8)
            | buffer[3];
    }

    private static ulong ReadUInt64BigEndian(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[8];
        if (stream.Read(buffer) != buffer.Length)
        {
            return 0;
        }

        return ((ulong)buffer[0] << 56)
            | ((ulong)buffer[1] << 48)
            | ((ulong)buffer[2] << 40)
            | ((ulong)buffer[3] << 32)
            | ((ulong)buffer[4] << 24)
            | ((ulong)buffer[5] << 16)
            | ((ulong)buffer[6] << 8)
            | buffer[7];
    }

    private static string ReadAscii(Stream stream, int length)
    {
        Span<byte> buffer = stackalloc byte[length];
        return stream.Read(buffer) == length
            ? System.Text.Encoding.ASCII.GetString(buffer)
            : "";
    }

    private static bool IsValidPlaybackFps(double fps)
    {
        return fps > 0 && fps < 300 && !double.IsNaN(fps) && !double.IsInfinity(fps);
    }

    private void TogglePlayback()
    {
        if (!_isPlaybackMode || _playbackCapture is null)
        {
            return;
        }

        _playbackPlaying = !_playbackPlaying;
        playbackControl.IsPlaying = _playbackPlaying;
        if (_playbackPlaying)
        {
            StartPlaybackLoop();
        }
        else
        {
            StopPlaybackLoop();
        }
    }

    private void StartPlaybackLoop()
    {
        StopPlaybackLoop();
        _playbackPlayCts = new CancellationTokenSource();
        _ = RunPlaybackLoopAsync(_playbackPlayCts.Token);
    }

    private void StopPlaybackLoop()
    {
        _playbackPlayCts?.Cancel();
        _playbackPlayCts?.Dispose();
        _playbackPlayCts = null;
    }

    private async Task RunPlaybackLoopAsync(CancellationToken token)
    {
        try
        {
            var intervalMs = Math.Clamp(1000.0 / Math.Max(1, _playbackFps), 1, 200);
            var stopwatch = Stopwatch.StartNew();
            var nextFrameAtMs = 0.0;

            while (!token.IsCancellationRequested && _isPlaybackMode && _playbackPlaying)
            {
                PlayNextPlaybackFrame();
                if (!_playbackPlaying)
                {
                    break;
                }

                nextFrameAtMs += intervalMs;
                var delayMs = nextFrameAtMs - stopwatch.Elapsed.TotalMilliseconds;
                if (delayMs > 1)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(delayMs), token);
                }
                else
                {
                    await Task.Yield();
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void StepPlaybackFrame(int delta)
    {
        if (!_isPlaybackMode)
        {
            return;
        }

        _playbackPlaying = false;
        StopPlaybackLoop();
        playbackControl.IsPlaying = false;
        ShowPlaybackFrame(_playbackFrameIndex + delta);
    }

    private void PlayNextPlaybackFrame()
    {
        if (!_isPlaybackMode)
        {
            return;
        }

        if (_playbackFrameCount > 0 && _playbackFrameIndex >= _playbackFrameCount - 1)
        {
            _playbackPlaying = false;
            StopPlaybackLoop();
            playbackControl.IsPlaying = false;
            return;
        }

        ShowPlaybackFrame(_playbackFrameIndex + 1, seek: false);
    }

    private void ShowPlaybackFrame(int frameIndex, bool seek = true)
    {
        if (_playbackCapture is null)
        {
            return;
        }

        if (_playbackFrameCount > 0)
        {
            frameIndex = Math.Clamp(frameIndex, 0, _playbackFrameCount - 1);
        }
        else
        {
            frameIndex = Math.Max(0, frameIndex);
        }

        if (seek)
        {
            _playbackCapture.Set(VideoCaptureProperties.PosFrames, frameIndex);
        }

        using var capturedFrame = new Mat();
        if (!_playbackCapture.Read(capturedFrame) || capturedFrame.Empty())
        {
            _playbackPlaying = false;
            StopPlaybackLoop();
            playbackControl.IsPlaying = false;
            return;
        }

        using var frame = capturedFrame.Clone();
        _playbackFrameIndex = frameIndex;
        _latestFrame?.Dispose();
        _latestFrame = frame.Clone();
        _lastFrameAt = DateTime.Now;
        var result = ShouldRunPlaybackAlgorithm()
            ? AnalyzeFrame(frame, DateTime.Now)
            : CreateBypassDetectionResult(DateTime.Now);
        using var preview = ShouldDrawPlaybackOverlay() ? DrawOverlay(frame, result) : frame.Clone();
        UpdatePreview(preview, result);
        UpdatePlaybackInfo();
        UpdatePlaybackTimeline();
    }

    private void RedrawPlaybackFrameIfActive()
    {
        if (!_isPlaybackMode || _playbackCapture is null)
        {
            return;
        }

        ShowPlaybackFrame(_playbackFrameIndex);
    }

    private bool ShouldRunPlaybackAlgorithm()
    {
        return ShouldDrawPlaybackOverlay();
    }

    private bool ShouldDrawPlaybackOverlay()
    {
        return (_settings.Overlay.ShowRodRoi && _settings.Overlay.ShowPlaybackRoiOutlines)
            || (_settings.Overlay.ShowRodRoi && _settings.Overlay.ShowPlaybackDiffMessage)
            || (_settings.Overlay.ShowRodRoi && _settings.Overlay.ShowPlaybackTrackingCandidate)
            || _settings.Overlay.ShowDebugText;
    }

    private void UpdatePlaybackInfo()
    {
        var total = _playbackFrameCount > 0 ? _playbackFrameCount.ToString() : "?";
        var time = _playbackFps > 0 ? _playbackFrameIndex / _playbackFps : 0;
        var totalSeconds = _playbackDurationSeconds > 0
            ? _playbackDurationSeconds
            : _playbackFps > 0 && _playbackFrameCount > 0
                ? _playbackFrameCount / _playbackFps
                : 0;
        playbackControl.SetPosition(_playbackFrameIndex, FormatPlaybackTime(time), FormatPlaybackTime(totalSeconds));
        lblRtspStatus.Text = $"File: {Path.GetFileName(_playbackPath)}  Frame {_playbackFrameIndex + 1}/{total}";
    }

    private void ConfigurePlaybackTimeline()
    {
        _updatingPlaybackTimeline = true;
        playbackControl.SetRange(0, Math.Max(0, _playbackFrameCount - 1));
        playbackControl.Value = 0;
        _updatingPlaybackTimeline = false;
    }

    private void UpdatePlaybackTimeline()
    {
        if (_playbackFrameCount <= 0)
        {
            return;
        }

        _updatingPlaybackTimeline = true;
        playbackControl.Value = Math.Clamp(_playbackFrameIndex, playbackControl.Minimum, playbackControl.Maximum);
        _updatingPlaybackTimeline = false;
    }

    private void SeekPlaybackFrame(int frameIndex)
    {
        if (_updatingPlaybackTimeline || !_isPlaybackMode)
        {
            return;
        }

        _playbackPlaying = false;
        StopPlaybackLoop();
        playbackControl.IsPlaying = false;
        ShowPlaybackFrame(frameIndex);
    }

    private static string FormatPlaybackTime(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
        {
            seconds = 0;
        }

        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours:0}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes:00}:{time.Seconds:00}";
    }

    private async Task CaptureLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (!_cameraService.TryRead(out var frame))
                {
                    _consecutiveFrameFailures++;
                    await HandleNoFrameAsync(token);
                    continue;
                }

                _lastFrameAt = DateTime.Now;
                _consecutiveFrameFailures = 0;
                using (frame)
                {
                    _latestFrame?.Dispose();
                    _latestFrame = frame.Clone();
                    var now = DateTime.Now;
                    _stateMachine.AutoRecordingEnabled = _isWatching && rdoAutoRecording.Checked;
                    var algorithmEnabled = _isWatching && ShouldRunAlgorithm();
                    var result = algorithmEnabled
                        ? AnalyzeFrame(frame, now)
                        : CreateBypassDetectionResult(now);
                    var stateResult = algorithmEnabled
                        ? _stateMachine.Update(result, now)
                        : new StateUpdateResult { NewState = _stateMachine.CurrentState };
                    if (!_recordingService.IsRecording && ((_isWatching && rdoAutoRecording.Checked) || _manualRecordingRequested))
                    {
                        _recordingService.AddToPreBuffer(frame, now);
                    }

                    if (_fullRecordingRequested)
                    {
                        RotateFullRecordingIfDue(now, frame);
                    }

                    if (stateResult.ShouldStartRecording)
                    {
                        if (!CanStartRecordingOnDisk(now, stateResult.TriggerReason))
                        {
                            _stateMachine.CompleteRecording(now);
                            _manualRecordingRequested = false;
                            BeginInvoke(UpdateControlStates);
                            continue;
                        }

                        _recordingService.StartRecording(now, stateResult.TriggerReason, frame);
                        ShowRecordingStamp("Recording started", Color.FromArgb(24, 132, 74));
                        _currentRecordingStartedAt = now;
                        _currentTriggerReason = stateResult.TriggerReason;
                        _maxMotionScore = 0;
                        _maxRodMotionScore = 0;
                        _minHomeDiffScore = double.MaxValue;
                        BeginInvoke(UpdateControlStates);
                    }

                    if (_recordingService.IsRecording)
                    {
                        _recordingService.WriteFrame(frame, now);
                        _maxMotionScore = Math.Max(_maxMotionScore, result.PersonMotionScore);
                        _maxRodMotionScore = Math.Max(_maxRodMotionScore, result.RodMotionScore);
                        _minHomeDiffScore = Math.Min(_minHomeDiffScore, result.HomeDiffScore);
                    }

                    if (stateResult.ShouldStopRecording && !_fullRecordingRequested)
                    {
                        var filePath = _recordingService.StopRecording(now);
                        ShowRecordingStamp("Recording stopped", Color.FromArgb(192, 92, 24));
                        SaveEventLog(filePath, now);
                        _stateMachine.CompleteRecording(now);
                        BeginInvoke(UpdateControlStates);
                    }

                    using var preview = ShouldDrawOverlay() ? DrawOverlay(frame, result, stateResult) : frame.Clone();
                    UpdatePreview(preview, result);
                }

                await Task.Yield();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Capture loop failed");
                BeginInvoke(() => lblErrorStatus.Text = $"Error: {ex.Message}");
                await Task.Delay(500, token);
            }
        }
    }

    private async Task HandleNoFrameAsync(CancellationToken token)
    {
        var timeout = TimeSpan.FromSeconds(_settings.Camera.NoFrameTimeoutSeconds);
        if (_consecutiveFrameFailures >= 10 || (_lastFrameAt != DateTime.MinValue && DateTime.Now - _lastFrameAt > timeout))
        {
            _cameraService.MarkReconnecting();
            BeginInvoke(() =>
            {
                lblCameraStatus.Text = "Camera: reconnecting";
                lblRtspStatus.Text = _settings.Camera.IsIpCamera ? "RTSP: Reconnecting" : "RTSP: n/a";
            });
            _cameraService.Close();
            await Task.Delay(TimeSpan.FromSeconds(_settings.Camera.ReconnectDelaySeconds), token);
            var opened = _cameraService.Open(_settings);
            BeginInvoke(() =>
            {
                lblCameraStatus.Text = opened ? GetCameraStatusText() : GetCameraErrorText();
                lblRtspStatus.Text = _settings.Camera.IsIpCamera ? $"RTSP: {_cameraService.ConnectionState}" : "RTSP: n/a";
            });
            _lastFrameAt = DateTime.Now;
            _consecutiveFrameFailures = 0;
        }
        else
        {
            await Task.Delay(100, token);
        }
    }

    private Mat DrawOverlay(Mat frame, DetectionResult result, StateUpdateResult? stateResult = null)
    {
        var output = frame.Clone();
        DrawPrivacyMasks(output);
        if (_settings.Overlay.ShowRodRoi && (!_isPlaybackMode || _settings.Overlay.ShowPlaybackRoiOutlines))
        {
            DrawRoi(output, ScaleRoiForFrame(_settings.Rois.PersonWatchRoi, output), Scalar.Yellow, "ROI", 2);
            DrawRoi(output, ScaleRoiForFrame(_settings.Rois.IgnoreRoi, output), new Scalar(180, 180, 180), "IGNORE_ROI", 3);
        }

        if (_settings.Overlay.ShowMotionMask)
        {
            foreach (var box in result.MotionBoxes)
            {
                Cv2.Rectangle(output, ToCvRect(box), Scalar.Orange, 2);
            }
        }

        if (_settings.Overlay.ShowPersonBox)
        {
            foreach (var box in result.PersonCandidateBoxes)
            {
                Cv2.Rectangle(output, ToCvRect(box), Scalar.Blue, 2);
            }
        }

        if (_recordingService.IsRecording && _settings.Overlay.ShowRecordingStatus)
        {
            Cv2.PutText(output, "REC", new OpenCvSharp.Point(24, 42), HersheyFonts.HersheySimplex, 1.2, Scalar.Red, 3);
        }

        if ((_isPlaybackMode && _settings.Overlay.ShowPlaybackTrackingCandidate) || (!_isPlaybackMode && _recordingService.IsRecording))
        {
            DrawTrackingCandidateOverlay(output, result);
        }

        if (_isPlaybackMode && _settings.Overlay.ShowPlaybackDiffMessage)
        {
            DrawPlaybackDiffOverlay(output, result);
        }

        if (_recordingService.IsRecording && stateResult is not null)
        {
            DrawRecordingHoldOverlay(output, result, stateResult);
        }

        if (_settings.Overlay.ShowDebugText)
        {
            Cv2.PutText(output, $"{_stateMachine.CurrentState} {result.DebugText}", new OpenCvSharp.Point(24, output.Height - 24), HersheyFonts.HersheySimplex, 0.6, Scalar.White, 2);
        }

        return output;
    }

    private void DrawRecordingHoldOverlay(Mat output, DetectionResult result, StateUpdateResult stateResult)
    {
        var reason = string.IsNullOrWhiteSpace(stateResult.RecordingHoldReason)
            ? "KEEP: unknown"
            : stateResult.RecordingHoldReason;
        var y = Math.Max(64, output.Height - 64);
        Cv2.Rectangle(output, new OpenCvSharp.Rect(18, y - 34, Math.Min(output.Width - 36, 760), 44), Scalar.Black, -1);
        Cv2.PutText(output, reason, new OpenCvSharp.Point(28, y), HersheyFonts.HersheySimplex, 0.8, Scalar.Cyan, 2);
    }

    private void DrawPlaybackDiffOverlay(Mat output, DetectionResult result)
    {
        var text = $"ROI_Diff {result.PersonMotionScore:0.000} / th {_settings.Detection.PersonMotionRatioThreshold:0.000}";
        var y = Math.Max(64, output.Height - 64);
        Cv2.Rectangle(output, new OpenCvSharp.Rect(18, y - 34, Math.Min(output.Width - 36, 520), 44), Scalar.Black, -1);
        Cv2.PutText(output, text, new OpenCvSharp.Point(28, y), HersheyFonts.HersheySimplex, 0.8, Scalar.Cyan, 2);
    }

    private void DrawTrackingCandidateOverlay(Mat output, DetectionResult result)
    {
        var candidate = FindLargestRecordingHoldCandidate(result);
        if (candidate.HasValue)
        {
            var candidateColor = result.PersonMotionScore >= _settings.Detection.PersonMotionRatioThreshold
                ? Scalar.Red
                : Scalar.Cyan;
            Cv2.Rectangle(output, ToCvRect(candidate.Value), candidateColor, 4);
            Cv2.PutText(
                output,
                "KEEP CANDIDATE",
                new OpenCvSharp.Point(candidate.Value.X + 6, Math.Max(22, candidate.Value.Y - 8)),
                HersheyFonts.HersheySimplex,
                0.7,
                candidateColor,
                2);
        }
    }

    private Rectangle? FindLargestRecordingHoldCandidate(DetectionResult result)
    {
        var frameWidth = _latestFrame?.Width ?? _settings.Camera.ActiveWidth;
        var frameHeight = _latestFrame?.Height ?? _settings.Camera.ActiveHeight;
        var mainRoi = DetectionService.ScaleRoiToFrame(_settings.Rois.PersonWatchRoi, _settings, frameWidth, frameHeight).ToRectangle();
        return result.MotionBoxes
            .Where(box => box.IntersectsWith(mainRoi))
            .OrderByDescending(box => box.Width * box.Height)
            .Cast<Rectangle?>()
            .FirstOrDefault();
    }

    private RoiRect ScaleRoiForFrame(RoiRect roi, Mat frame)
    {
        return DetectionService.ScaleRoiToFrame(roi, _settings, frame.Width, frame.Height);
    }

    private DetectionResult AnalyzeFrame(Mat frame, DateTime now)
    {
        var sw = Stopwatch.StartNew();
        var result = _detectionService.Analyze(frame, _settings);
        sw.Stop();
        _lastAlgorithmMs = sw.Elapsed.TotalMilliseconds;
        _lastAlgorithmEnabled = true;
        _algorithmFpsCounter.Tick();
        result.Timestamp = now;
        return result;
    }

    private DetectionResult CreateBypassDetectionResult(DateTime now)
    {
        _lastAlgorithmEnabled = false;
        _lastAlgorithmMs = 0;
        return new DetectionResult
        {
            Timestamp = now,
            HomeStable = true,
            HomeSimilar = true,
            HomeMotionLow = true,
            DebugText = "algorithm off"
        };
    }

    private bool ShouldRunAlgorithm()
    {
        return rdoAutoRecording.Checked
            || _recordingService.IsRecording
            || _settings.Overlay.ShowDebugText;
    }

    private bool ShouldDrawOverlay()
    {
        return _settings.Overlay.ShowRodRoi
            || _settings.Overlay.ShowDebugText
            || _recordingService.IsRecording;
    }

    private void UpdatePreview(Mat preview, DetectionResult result)
    {
        if (Interlocked.Exchange(ref _previewUpdatePending, 1) == 1)
        {
            return;
        }

        _fpsCounter.Tick();
        var bitmap = BitmapConverter.ToBitmap(preview);
        DrawActiveRecordingStamp(bitmap);
        DrawRecordingStamp(bitmap);
        if (IsDisposed || !IsHandleCreated)
        {
            bitmap.Dispose();
            Interlocked.Exchange(ref _previewUpdatePending, 0);
            return;
        }

        if (_isPlaybackMode && !InvokeRequired)
        {
            ApplyPreviewBitmap(bitmap, result);
            Interlocked.Exchange(ref _previewUpdatePending, 0);
            return;
        }

        BeginInvoke(() =>
        {
            if (IsDisposed)
            {
                bitmap.Dispose();
                Interlocked.Exchange(ref _previewUpdatePending, 0);
                return;
            }

            ApplyPreviewBitmap(bitmap, result);
            Interlocked.Exchange(ref _previewUpdatePending, 0);
        });
    }

    private void ApplyPreviewBitmap(Bitmap bitmap, DetectionResult result)
    {
        var old = picCameraPreview.Image;
        picCameraPreview.Image = bitmap;
        old?.Dispose();
        lblFps.Text = $"FPS: {_fpsCounter.CurrentFps:0.0}";
        if (_isPlaybackMode)
        {
            lblCameraStatus.Text = _playbackPlaying ? "Playback: playing" : "Playback: paused";
            lblRtspStatus.Text = $"File: {Path.GetFileName(_playbackPath)}";
            lblLastFrame.Text = _playbackFrameCount > 0 ? $"Frame: {_playbackFrameIndex + 1}/{_playbackFrameCount}" : $"Frame: {_playbackFrameIndex + 1}";
        }
        else
        {
            lblCameraStatus.Text = GetCameraStatusText();
            lblRtspStatus.Text = _settings.Camera.IsIpCamera ? $"RTSP: {_cameraService.ConnectionState}" : "RTSP: n/a";
            lblLastFrame.Text = _lastFrameAt == DateTime.MinValue ? "Last Frame: n/a" : $"Last Frame: {(DateTime.Now - _lastFrameAt).TotalSeconds:0.0}s ago";
        }

        lblDetectionStatus.Text = $"Detection: P={result.PersonDetected} I={result.RodMotionScore:0.000} H={result.HomeStable}";
        lblAlgorithmStatus.Text = _lastAlgorithmEnabled ? $"Algo: {_lastAlgorithmMs:0.0}ms / {_algorithmFpsCounter.CurrentFps:0.0}fps" : "Algo: off";
        lblRecordingStatus.Text = _isPlaybackMode ? "Recording: playback mode" : _recordingService.IsRecording ? "Recording: on" : $"Recording: {_stateMachine.CurrentState}";
        UpdateControlStates();
        if (DateTime.Now - _lastDiskStatusAt > TimeSpan.FromSeconds(5))
        {
            _cachedDiskStatus = $"Disk: {DiskUtils.GetUsedPercent(_paths.RecVideos):0.0}% used / Free: {DiskUtils.FormatBytes(DiskUtils.GetFreeDiskBytes(_paths.RecVideos))}";
            _lastDiskStatusAt = DateTime.Now;
        }

        lblDiskStatus.Text = _cachedDiskStatus;
        lblErrorStatus.Text = "Error: none";
    }

    private void SaveEventLog(string filePath, DateTime endTime)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        _eventLogService.Append(new Models.EventLog
        {
            StartTime = _currentRecordingStartedAt,
            EndTime = endTime,
            FilePath = filePath,
            TriggerReason = _currentTriggerReason,
            MaxMotionScore = _maxMotionScore,
            MaxRodMotionScore = _maxRodMotionScore,
            MinHomeDiffScore = _minHomeDiffScore,
            ManualRecording = string.Equals(_currentTriggerReason, "Manual", StringComparison.OrdinalIgnoreCase)
        });
    }

    private bool CanStartRecordingOnDisk(DateTime now, string triggerReason)
    {
        var usedPercent = DiskUtils.GetUsedPercent(_paths.RecVideos);
        var stopThreshold = Math.Clamp(_settings.Storage.DiskStopThresholdPercent, 1, 100);
        var resumeThreshold = Math.Clamp(_settings.Storage.DiskResumeThresholdPercent, 1, stopThreshold);
        if (_recordingBlockedByDisk && usedPercent > resumeThreshold)
        {
            return BlockRecordingForDisk(now, triggerReason, usedPercent, stopThreshold, resumeThreshold);
        }

        if (usedPercent < stopThreshold)
        {
            _recordingBlockedByDisk = false;
            return true;
        }

        _recordingBlockedByDisk = true;
        return BlockRecordingForDisk(now, triggerReason, usedPercent, stopThreshold, resumeThreshold);
    }

    private bool BlockRecordingForDisk(DateTime now, string triggerReason, double usedPercent, int stopThreshold, int resumeThreshold)
    {
        lblRecordingStatus.Text = $"Recording: disk {usedPercent:0.0}% full";
        ShowRecordingStamp("Disk full", Color.FromArgb(178, 34, 34));
        if (now - _lastDiskFullLogAt > TimeSpan.FromMinutes(1))
        {
            _logger.Info($"Recording blocked. Disk used {usedPercent:0.0}%, stop={stopThreshold}%, resume={resumeThreshold}% ({triggerReason}).");
            _lastDiskFullLogAt = now;
        }

        return false;
    }

    private void StartCleanupSchedule(bool runStartupCleanup = true)
    {
        if (runStartupCleanup && _settings.Storage.CleanupOnStartup)
        {
            RunStorageCleanup("startup");
        }

        _cleanupTimer?.Dispose();
        _cleanupTimer = new System.Windows.Forms.Timer { Interval = 60000 };
        _cleanupTimer.Tick += (_, _) =>
        {
            var now = DateTime.Now;
            var cleanupHour = Math.Clamp(_settings.Storage.CleanupHour, 0, 23);
            if (_lastCleanupDate.Date == now.Date || now.Hour != cleanupHour)
            {
                return;
            }

            RunStorageCleanup("daily");
            _lastCleanupDate = now.Date;
        };
        _cleanupTimer.Start();
    }

    private void RunStorageCleanup(string reason)
    {
        if (_recordingService?.IsRecording == true)
        {
            _logger.Info($"Storage cleanup skipped ({reason}): recording is active.");
            return;
        }

        try
        {
            var result = _cleanupService.Cleanup(_settings.Storage);
            _logger.Info($"Storage cleanup completed ({reason}). Deleted={result.DeletedFiles}, Failed={result.FailedFiles}, Freed={DiskUtils.FormatBytes(result.FreedBytes)}.");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Storage cleanup failed ({reason})");
        }
    }

    private void ReadCameraSettingsFromUi()
    {
        _settings.Camera.CameraType = rdoIpCamera.Checked ? "IP" : "USB";
        _settings.Camera.IpCamera.IpAddress = txtIpAddress.Text.Trim();
        _settings.Camera.IpCamera.RtspPort = (int)numRtspPort.Value;
        _settings.Camera.IpCamera.HttpPort = (int)numHttpPort.Value;
        _settings.Camera.IpCamera.Username = "";
        _settings.Camera.IpCamera.Password = "";
        _settings.Camera.IpCamera.StreamPath = cmbStreamPath.Text.Trim();
        _settings.Camera.IpCamera.UseManualRtspUrl = chkUseManualRtspUrl.Checked;
        _settings.Camera.IpCamera.ManualRtspUrl = txtManualRtspUrl.Text.Trim();
        if (cmbCameraList.SelectedItem is int index)
        {
            _settings.Camera.UsbCamera.DeviceIndex = index;
        }

        if (cmbResolution.SelectedItem is string resolution)
        {
            var parts = resolution.Split('x');
            if (_settings.Camera.IsIpCamera)
            {
                _settings.Camera.IpCamera.Width = int.Parse(parts[0]);
                _settings.Camera.IpCamera.Height = int.Parse(parts[1]);
            }
            else
            {
                _settings.Camera.UsbCamera.Width = int.Parse(parts[0]);
                _settings.Camera.UsbCamera.Height = int.Parse(parts[1]);
            }
        }

        if (cmbFps.SelectedItem is string fps)
        {
            if (_settings.Camera.IsIpCamera)
            {
                _settings.Camera.IpCamera.Fps = int.Parse(fps);
            }
            else
            {
                _settings.Camera.UsbCamera.Fps = int.Parse(fps);
            }
        }

        SyncOverlaySettings();
        ReadDetectionSettingsFromUi();
        UpdateRtspPreview();
    }

    private void ReadDetectionSettingsFromUi()
    {
        _settings.Detection.PersonMotionRatioThreshold = (double)numPersonThreshold.Value;
    }

    private void ReadRecordingSettingsFromUi()
    {
        _settings.Recording.Mode = rdoFullRecording.Checked
            ? "Full"
            : rdoAutoRecording.Checked
                ? "Auto"
                : "Manual";
    }

    private void SaveRecordingModeFromUi()
    {
        if (_settingsManager is null)
        {
            return;
        }

        ReadRecordingSettingsFromUi();
        _settingsManager.Save(_settings);
    }

    private void UpdateRtspPreview()
    {
        var cam = new IpCameraSettings
        {
            IpAddress = txtIpAddress.Text.Trim(),
            RtspPort = (int)numRtspPort.Value,
            HttpPort = (int)numHttpPort.Value,
            Username = "",
            Password = "",
            StreamPath = cmbStreamPath.Text.Trim(),
            UseManualRtspUrl = chkUseManualRtspUrl.Checked,
            ManualRtspUrl = txtManualRtspUrl.Text.Trim()
        };
        txtGeneratedRtspUrl.Text = RtspUrlBuilder.Build(cam);
    }

    private void OpenSettingsDialog()
    {
        using var form = new SettingsForm(_settings, ApplySettingsFromDialog, rdoFullRecording.Checked);
        form.ShowDialog(this);
    }

    private void ApplySettingsFromDialog()
    {
        ApplySettingsToUi();
        _settingsManager.Save(_settings);
        StartCleanupSchedule(runStartupCleanup: false);
        lblCameraStatus.Text = "Settings: saved";
        RedrawPlaybackFrameIfActive();
    }

    private void ScheduleAutoStartFullRecording()
    {
        if (_autoStartFullRecordingScheduled
            || !ShouldAutoStartFullRecording())
        {
            return;
        }

        _autoStartFullRecordingScheduled = true;
        _logger?.Info("Full auto start scheduled.");
        if (IsHandleCreated && !IsDisposed)
        {
            BeginInvoke(async () => await AutoStartFullRecordingAsync());
            return;
        }

        _autoStartFullRecordingTimer?.Dispose();
        _autoStartFullRecordingTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _autoStartFullRecordingTimer.Tick += async (_, _) =>
        {
            _autoStartFullRecordingTimer?.Stop();
            _autoStartFullRecordingTimer?.Dispose();
            _autoStartFullRecordingTimer = null;
            await AutoStartFullRecordingAsync();
        };
        _autoStartFullRecordingTimer.Start();
    }

    private async Task AutoStartFullRecordingAsync()
    {
        if (_autoStartFullRecordingRunning
            || !ShouldAutoStartFullRecording())
        {
            return;
        }

        _autoStartFullRecordingRunning = true;
        try
        {
            _logger.Info("Full auto start running.");
            lblRecordingStatus.Text = "Recording: auto start pending";
            rdoFullRecording.Checked = true;
            await Task.Delay(TimeSpan.FromSeconds(2));
            ReadCameraSettingsFromUi();
            if (!_settings.Camera.IsIpCamera && !await EnsureUsbCameraSelectedAsync())
            {
                lblRecordingStatus.Text = "Recording: auto start failed, no USB camera";
                _logger.Info("Full auto start skipped: no USB camera found.");
                return;
            }

            await OpenCameraPreviewAsync();
            if (!await WaitForFirstCameraFrameAsync(TimeSpan.FromSeconds(20)))
            {
                lblRecordingStatus.Text = "Recording: auto start failed, no frame";
                _logger.Info("Full auto start skipped: no frame received after camera open.");
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
            await StartFullRecordingAsync();
            if (_startInTray && _recordingService.IsRecording)
            {
                HideToTray();
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Full auto start failed");
            lblRecordingStatus.Text = $"Recording: auto start failed ({ex.Message})";
        }
        finally
        {
            _autoStartFullRecordingRunning = false;
            UpdateControlStates();
        }
    }

    private bool ShouldAutoStartFullRecording()
    {
        return string.Equals(_settings.Recording.Mode, "Full", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> WaitForFirstCameraFrameAsync(TimeSpan timeout)
    {
        var until = DateTime.Now + timeout;
        while (DateTime.Now < until)
        {
            if (IsDisposed)
            {
                return false;
            }

            if (_latestFrame is not null && _lastFrameAt != DateTime.MinValue && DateTime.Now - _lastFrameAt < TimeSpan.FromSeconds(3))
            {
                return true;
            }

            await Task.Delay(200);
        }

        return false;
    }

    private void UpdateCameraTypeUi()
    {
        var usb = rdoUsbCamera.Checked;
        foreach (var control in new Control[] { cmbCameraList, btnRefreshCamera, btnCameraProperty })
        {
            control.Enabled = usb;
        }

        foreach (var control in new Control[] { txtIpAddress, numRtspPort, numHttpPort, cmbStreamPath, chkUseManualRtspUrl, txtGeneratedRtspUrl })
        {
            control.Enabled = !usb;
        }

        txtManualRtspUrl.Enabled = !usb && chkUseManualRtspUrl.Checked;
        UpdateControlStates();
    }

    private async Task<bool> EnsureUsbCameraSelectedAsync()
    {
        if (_settings.Camera.IsIpCamera)
        {
            return true;
        }

        if (cmbCameraList.SelectedItem is int selectedIndex)
        {
            _settings.Camera.UsbCamera.DeviceIndex = selectedIndex;
            return true;
        }

        await RefreshCameraListAsync();
        if (cmbCameraList.SelectedItem is int refreshedIndex)
        {
            _settings.Camera.UsbCamera.DeviceIndex = refreshedIndex;
            return true;
        }

        return false;
    }

    private string GetCameraStatusText()
    {
        return _settings.Camera.IsIpCamera
            ? $"Camera: IP {_settings.Camera.IpCamera.IpAddress}"
            : $"Camera: USB {_settings.Camera.UsbCamera.DeviceIndex}";
    }

    private string GetCameraErrorText()
    {
        return _settings.Camera.IsIpCamera
            ? "Camera Error: IP camera connection failed"
            : "Camera Error: USB camera connection failed";
    }

    private void SyncOverlaySettings()
    {
        _settings.Overlay.ShowPersonBox = false;
        _settings.Overlay.ShowMotionMask = false;
        _settings.Overlay.ShowRodRoi = chkShowRodRoi.Checked;
        _settings.Overlay.ShowHomeRoi = false;
        _settings.Overlay.ShowDebugText = chkShowDebugText.Checked;
        _settings.Overlay.ShowRecordingStatus = false;
        _settings.Overlay.ShowFrameTime = false;
    }

    private void PicCameraPreview_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !chkShowRodRoi.Checked || TryMapPreviewPointToFrame(e.Location, out var framePoint) == false)
        {
            return;
        }

        var ignoreRoi = GetEditableFrameRoi(RoiEditTarget.IgnoreRoi).ToRectangle();
        var mainRoi = GetEditableFrameRoi(RoiEditTarget.MainRoi).ToRectangle();
        _roiEditTarget = HitTestRoi(framePoint, ignoreRoi) != RoiEditMode.None
            ? RoiEditTarget.IgnoreRoi
            : RoiEditTarget.MainRoi;
        _roiDragStartRect = _roiEditTarget == RoiEditTarget.IgnoreRoi ? ignoreRoi : mainRoi;
        _roiEditMode = HitTestRoi(framePoint, _roiDragStartRect);
        _roiSelected = _roiEditMode != RoiEditMode.None;
        if (!_roiSelected)
        {
            picCameraPreview.Cursor = Cursors.Default;
            return;
        }

        _isEditingRoi = true;
        _roiDragStartFramePoint = framePoint;
        picCameraPreview.Cursor = CursorForMode(_roiEditMode);
    }

    private void PicCameraPreview_MouseMove(object? sender, MouseEventArgs e)
    {
        if (!chkShowRodRoi.Checked || !TryMapPreviewPointToFrame(e.Location, out var framePoint))
        {
            picCameraPreview.Cursor = Cursors.Default;
            return;
        }

        if (!_isEditingRoi)
        {
            var rodMode = HitTestRoi(framePoint, GetEditableFrameRoi(RoiEditTarget.IgnoreRoi).ToRectangle());
            var mainMode = rodMode == RoiEditMode.None ? HitTestRoi(framePoint, GetEditableFrameRoi(RoiEditTarget.MainRoi).ToRectangle()) : RoiEditMode.None;
            picCameraPreview.Cursor = CursorForMode(rodMode != RoiEditMode.None ? rodMode : mainMode);
            return;
        }

        var rect = ApplyRoiEdit(_roiDragStartRect, _roiDragStartFramePoint, framePoint, _roiEditMode);
        rect = ClampRoiRect(rect);
        if (rect.Width < 8 || rect.Height < 8)
        {
            return;
        }

        SetEditableFrameRoi(_roiEditTarget, RoiRect.FromRectangle(rect));
    }

    private void PicCameraPreview_MouseUp(object? sender, MouseEventArgs e)
    {
        _isEditingRoi = false;
        _roiEditMode = RoiEditMode.None;
        _roiEditTarget = RoiEditTarget.None;
    }

    private RoiRect GetEditableRoi(RoiEditTarget target) => target == RoiEditTarget.IgnoreRoi
        ? _settings.Rois.IgnoreRoi
        : _settings.Rois.PersonWatchRoi;

    private RoiRect GetEditableFrameRoi(RoiEditTarget target)
    {
        var roi = GetEditableRoi(target);
        var image = picCameraPreview.Image;
        return image is null
            ? roi
            : DetectionService.ScaleRoiToFrame(roi, _settings, image.Width, image.Height);
    }

    private void SetEditableRoi(RoiEditTarget target, RoiRect roi)
    {
        if (target == RoiEditTarget.IgnoreRoi)
        {
            _settings.Rois.IgnoreRoi = roi;
            return;
        }

        _settings.Rois.PersonWatchRoi = roi;
    }

    private void SetEditableFrameRoi(RoiEditTarget target, RoiRect frameRoi)
    {
        var image = picCameraPreview.Image;
        if (image is null)
        {
            SetEditableRoi(target, frameRoi);
            return;
        }

        var sourceSize = GetRoiSourceSize();
        if (sourceSize.Width == image.Width && sourceSize.Height == image.Height)
        {
            SetEditableRoi(target, frameRoi);
            return;
        }

        SetEditableRoi(target, new RoiRect
        {
            X = (int)Math.Round(frameRoi.X * sourceSize.Width / (double)Math.Max(1, image.Width)),
            Y = (int)Math.Round(frameRoi.Y * sourceSize.Height / (double)Math.Max(1, image.Height)),
            Width = (int)Math.Round(frameRoi.Width * sourceSize.Width / (double)Math.Max(1, image.Width)),
            Height = (int)Math.Round(frameRoi.Height * sourceSize.Height / (double)Math.Max(1, image.Height))
        });
    }

    private System.Drawing.Size GetRoiSourceSize()
    {
        var rois = new[] { _settings.Rois.PersonWatchRoi, _settings.Rois.IgnoreRoi, _settings.Rois.RodHomeRoi };
        var width = Math.Max(1, Math.Max(_settings.Camera.ActiveWidth, rois.Max(roi => roi.X + roi.Width)));
        var height = Math.Max(1, Math.Max(_settings.Camera.ActiveHeight, rois.Max(roi => roi.Y + roi.Height)));
        return new System.Drawing.Size(width, height);
    }

    private RoiEditMode HitTestRoi(System.Drawing.Point point, Rectangle rect)
    {
        var tolerance = Math.Max(8, Math.Min(rect.Width, rect.Height) / 20);
        var nearLeft = Math.Abs(point.X - rect.Left) <= tolerance;
        var nearRight = Math.Abs(point.X - rect.Right) <= tolerance;
        var nearTop = Math.Abs(point.Y - rect.Top) <= tolerance;
        var nearBottom = Math.Abs(point.Y - rect.Bottom) <= tolerance;

        if (nearLeft && nearTop) return RoiEditMode.TopLeft;
        if (nearRight && nearTop) return RoiEditMode.TopRight;
        if (nearLeft && nearBottom) return RoiEditMode.BottomLeft;
        if (nearRight && nearBottom) return RoiEditMode.BottomRight;
        if (nearLeft && point.Y >= rect.Top && point.Y <= rect.Bottom) return RoiEditMode.Left;
        if (nearRight && point.Y >= rect.Top && point.Y <= rect.Bottom) return RoiEditMode.Right;
        if (nearTop && point.X >= rect.Left && point.X <= rect.Right) return RoiEditMode.Top;
        if (nearBottom && point.X >= rect.Left && point.X <= rect.Right) return RoiEditMode.Bottom;
        return rect.Contains(point) ? RoiEditMode.Move : RoiEditMode.None;
    }

    private static Cursor CursorForMode(RoiEditMode mode)
    {
        return mode switch
        {
            RoiEditMode.Move => Cursors.SizeAll,
            RoiEditMode.Left or RoiEditMode.Right => Cursors.SizeWE,
            RoiEditMode.Top or RoiEditMode.Bottom => Cursors.SizeNS,
            RoiEditMode.TopLeft or RoiEditMode.BottomRight => Cursors.SizeNWSE,
            RoiEditMode.TopRight or RoiEditMode.BottomLeft => Cursors.SizeNESW,
            _ => Cursors.Default
        };
    }

    private static Rectangle ApplyRoiEdit(Rectangle startRect, System.Drawing.Point startPoint, System.Drawing.Point currentPoint, RoiEditMode mode)
    {
        var dx = currentPoint.X - startPoint.X;
        var dy = currentPoint.Y - startPoint.Y;
        var left = startRect.Left;
        var right = startRect.Right;
        var top = startRect.Top;
        var bottom = startRect.Bottom;

        switch (mode)
        {
            case RoiEditMode.Move:
                left += dx;
                right += dx;
                top += dy;
                bottom += dy;
                break;
            case RoiEditMode.Left:
            case RoiEditMode.TopLeft:
            case RoiEditMode.BottomLeft:
                left += dx;
                break;
            case RoiEditMode.Right:
            case RoiEditMode.TopRight:
            case RoiEditMode.BottomRight:
                right += dx;
                break;
        }

        switch (mode)
        {
            case RoiEditMode.Top:
            case RoiEditMode.TopLeft:
            case RoiEditMode.TopRight:
                top += dy;
                break;
            case RoiEditMode.Bottom:
            case RoiEditMode.BottomLeft:
            case RoiEditMode.BottomRight:
                bottom += dy;
                break;
        }

        if (right < left)
        {
            (left, right) = (right, left);
        }

        if (bottom < top)
        {
            (top, bottom) = (bottom, top);
        }

        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    private Rectangle ClampRoiRect(Rectangle rect)
    {
        var image = picCameraPreview.Image;
        if (image is null)
        {
            return rect;
        }

        var x = Math.Clamp(rect.X, 0, Math.Max(0, image.Width - 1));
        var y = Math.Clamp(rect.Y, 0, Math.Max(0, image.Height - 1));
        var right = Math.Clamp(rect.Right, x + 1, image.Width);
        var bottom = Math.Clamp(rect.Bottom, y + 1, image.Height);
        return Rectangle.FromLTRB(x, y, right, bottom);
    }

    private bool TryMapPreviewPointToFrame(System.Drawing.Point previewPoint, out System.Drawing.Point framePoint)
    {
        framePoint = System.Drawing.Point.Empty;
        var image = picCameraPreview.Image;
        if (image is null)
        {
            return false;
        }

        var imageAspect = image.Width / (double)image.Height;
        var boxAspect = picCameraPreview.Width / (double)picCameraPreview.Height;
        int displayWidth;
        int displayHeight;
        int offsetX;
        int offsetY;
        if (imageAspect > boxAspect)
        {
            displayWidth = picCameraPreview.Width;
            displayHeight = (int)(displayWidth / imageAspect);
            offsetX = 0;
            offsetY = (picCameraPreview.Height - displayHeight) / 2;
        }
        else
        {
            displayHeight = picCameraPreview.Height;
            displayWidth = (int)(displayHeight * imageAspect);
            offsetX = (picCameraPreview.Width - displayWidth) / 2;
            offsetY = 0;
        }

        if (previewPoint.X < offsetX || previewPoint.Y < offsetY || previewPoint.X >= offsetX + displayWidth || previewPoint.Y >= offsetY + displayHeight)
        {
            return false;
        }

        var x = (int)((previewPoint.X - offsetX) * image.Width / (double)displayWidth);
        var y = (int)((previewPoint.Y - offsetY) * image.Height / (double)displayHeight);
        framePoint = new System.Drawing.Point(Math.Clamp(x, 0, image.Width - 1), Math.Clamp(y, 0, image.Height - 1));
        return true;
    }

    private async Task StartSelectedRecordingAsync()
    {
        if (!IsCameraPreviewOpen())
        {
            lblRecordingStatus.Text = "Recording: open camera first";
            UpdateControlStates();
            return;
        }

        if (rdoFullRecording.Checked)
        {
            await StartFullRecordingAsync();
            return;
        }

        await StartManualRecordingAsync();
    }

    private Task StartFullRecordingAsync()
    {
        if (_recordingService.IsRecording || !IsCameraPreviewOpen())
        {
            return Task.CompletedTask;
        }

        _stateMachine.RequestManualRecordingStop();
        _manualRecordingRequested = false;
        _fullRecordingRequested = true;
        rdoFullRecording.Checked = true;
        if (!_recordingService.IsRecording && _cameraService.IsOpened)
        {
            RotateFullRecordingIfDue(DateTime.Now, _latestFrame);
        }

        lblRecordingStatus.Text = $"Recording: full every {GetFullInterval().TotalMinutes:0}m";
        UpdateControlStates();
        return Task.CompletedTask;
    }

    private void StopSelectedRecording()
    {
        _stateMachine.RequestManualRecordingStop();
        _manualRecordingRequested = false;
        _fullRecordingRequested = false;
        _nextFullRecordingRotationAt = DateTime.MinValue;
        StopRecordingIfActive(DateTime.Now);
        lblRecordingStatus.Text = "Recording: stopped";
        UpdateControlStates();
    }

    private void RotateFullRecordingIfDue(DateTime now, Mat? frame)
    {
        if (_recordingService.IsRecording && now < _nextFullRecordingRotationAt)
        {
            return;
        }

        if (_recordingService.IsRecording)
        {
            var filePath = _recordingService.StopRecording(now);
            ShowRecordingStamp("Recording stopped", Color.FromArgb(192, 92, 24));
            SaveEventLog(filePath, now);
        }

        if (!CanStartRecordingOnDisk(now, "Full"))
        {
            _fullRecordingRequested = false;
            _nextFullRecordingRotationAt = DateTime.MinValue;
            BeginInvoke(UpdateControlStates);
            return;
        }

        _recordingService.StartRecording(now, "Full", frame);
        ShowRecordingStamp("Recording started", Color.FromArgb(24, 132, 74));
        _currentRecordingStartedAt = now;
        _currentTriggerReason = "Full";
        _maxMotionScore = 0;
        _maxRodMotionScore = 0;
        _minHomeDiffScore = double.MaxValue;
        _nextFullRecordingRotationAt = now.Add(GetFullInterval());
        BeginInvoke(UpdateControlStates);
    }

    private TimeSpan GetFullInterval()
    {
        return TimeSpan.FromMinutes(Math.Clamp(_settings.Recording.FullIntervalMinutes, 1, 1440));
    }

    private Task StartManualRecordingAsync()
    {
        if (_recordingService.IsRecording || !IsCameraPreviewOpen())
        {
            return Task.CompletedTask;
        }

        _fullRecordingRequested = false;
        _nextFullRecordingRotationAt = DateTime.MinValue;
        _stateMachine.RequestManualRecordingStart();
        _manualRecordingRequested = true;
        rdoManualRecording.Checked = true;
        if (!_recordingService.IsRecording && _cameraService.IsOpened)
        {
            var now = DateTime.Now;
            if (!CanStartRecordingOnDisk(now, "Manual"))
            {
                _stateMachine.RequestManualRecordingStop();
                _manualRecordingRequested = false;
                BeginInvoke(UpdateControlStates);
                return Task.CompletedTask;
            }

            _recordingService.StartRecording(now, "Manual", _latestFrame);
            ShowRecordingStamp("Recording started", Color.FromArgb(24, 132, 74));
            _currentRecordingStartedAt = now;
            _currentTriggerReason = "Manual";
            _maxMotionScore = 0;
            _maxRodMotionScore = 0;
            _minHomeDiffScore = double.MaxValue;
        }

        lblRecordingStatus.Text = "Recording: manual requested";
        UpdateControlStates();
        return Task.CompletedTask;
    }

    private void StopManualRecording()
    {
        _stateMachine.RequestManualRecordingStop();
        _manualRecordingRequested = false;
        StopRecordingIfActive(DateTime.Now);
        lblRecordingStatus.Text = "Recording: stopped";
        UpdateControlStates();
    }

    private void StopRecordingIfActive(DateTime endTime)
    {
        _stateMachine.RequestManualRecordingStop();
        _manualRecordingRequested = false;
        _fullRecordingRequested = false;
        _nextFullRecordingRotationAt = DateTime.MinValue;
        if (!_recordingService.IsRecording)
        {
            return;
        }

        var filePath = _recordingService.StopRecording(endTime);
        ShowRecordingStamp("Recording stopped", Color.FromArgb(192, 92, 24));
        SaveEventLog(filePath, endTime);
        _stateMachine.CompleteRecording(endTime);
        UpdateControlStates();
    }

    private void ShowRecordingStamp(string text, Color backColor)
    {
        _recordingStampText = text;
        _recordingStampBackColor = backColor;
        _recordingStampUntil = DateTime.Now.AddSeconds(3);
    }

    private void DrawRecordingStamp(Bitmap bitmap)
    {
        if (string.IsNullOrWhiteSpace(_recordingStampText) || DateTime.Now > _recordingStampUntil)
        {
            return;
        }

        using var graphics = Graphics.FromImage(bitmap);
        using var font = new Font("Malgun Gothic", 18F, FontStyle.Bold);
        var padding = 12;
        var textSize = graphics.MeasureString(_recordingStampText, font);
        var rect = new RectangleF(18, 18, textSize.Width + padding * 2, textSize.Height + padding);
        using var background = new SolidBrush(Color.FromArgb(220, _recordingStampBackColor));
        using var foreground = new SolidBrush(Color.White);
        graphics.FillRectangle(background, rect);
        graphics.DrawRectangle(Pens.White, Rectangle.Round(rect));
        graphics.DrawString(_recordingStampText, font, foreground, rect.X + padding, rect.Y + padding / 2f);
    }

    private void DrawActiveRecordingStamp(Bitmap bitmap)
    {
        if (_recordingService?.IsRecording != true)
        {
            return;
        }

        var blinkOn = (DateTime.Now.Millisecond / 350) % 2 == 0;
        if (!blinkOn)
        {
            return;
        }

        const string text = "REC";
        using var graphics = Graphics.FromImage(bitmap);
        using var font = new Font("Malgun Gothic", 20F, FontStyle.Bold);
        var padding = 12;
        var textSize = graphics.MeasureString(text, font);
        var rect = new RectangleF(bitmap.Width - textSize.Width - padding * 2 - 18, 18, textSize.Width + padding * 2, textSize.Height + padding);
        using var background = new SolidBrush(Color.FromArgb(230, 190, 24, 32));
        using var foreground = new SolidBrush(Color.White);
        graphics.FillRectangle(background, rect);
        graphics.DrawRectangle(Pens.White, Rectangle.Round(rect));
        graphics.DrawString(text, font, foreground, rect.X + padding, rect.Y + padding / 2f);
    }

    private bool IsCameraPreviewOpen() => _captureCts is not null;

    private void UpdateControlStates()
    {
        if (_recordingService is null)
        {
            btnConnectCamera.Enabled = false;
            btnDisconnectCamera.Enabled = false;
            btnOpenCamera.Enabled = false;
            btnCloseCamera.Enabled = false;
            btnSaveHomeReference.Enabled = false;
            btnWatchToggle.Enabled = false;
            btnStartRecording.Enabled = false;
            btnStopRecording.Enabled = false;
            playbackControl.Enabled = false;
            return;
        }

        var previewOpen = IsCameraPreviewOpen();
        var recording = _recordingService.IsRecording;
        var usb = rdoUsbCamera.Checked;
        var cameraActionAvailable = !recording && !_cameraListRefreshInProgress;

        btnRefreshCamera.Enabled = usb && !_cameraListRefreshInProgress;
        btnCameraProperty.Enabled = usb && cmbCameraList.SelectedItem is int;
        btnConnectCamera.Enabled = cameraActionAvailable && !_cameraService.IsOpened;
        btnDisconnectCamera.Enabled = !_isPlaybackMode && (_cameraService.IsOpened || previewOpen);
        btnOpenCamera.Enabled = cameraActionAvailable && !previewOpen;
        btnCloseCamera.Enabled = !_isPlaybackMode && previewOpen;
        btnSaveHomeReference.Enabled = !_isPlaybackMode
            && previewOpen
            && _latestFrame is not null
            && _lastFrameAt != DateTime.MinValue
            && DateTime.Now - _lastFrameAt < TimeSpan.FromSeconds(3);

        if (_isPlaybackMode)
        {
            btnSaveHomeReference.Enabled = false;
            btnWatchToggle.Enabled = false;
            btnStartRecording.Enabled = false;
            btnStopRecording.Enabled = false;
            playbackControl.Enabled = true;
            return;
        }

        btnWatchToggle.Enabled = previewOpen && !rdoFullRecording.Checked;
        btnStartRecording.Enabled = previewOpen && !recording;
        btnStopRecording.Enabled = recording;
        playbackControl.Enabled = false;
        if (!previewOpen && _isWatching)
        {
            SetWatching(false);
        }
    }

    private void SaveHomeReference()
    {
        if (_latestFrame is null)
        {
            return;
        }

        try
        {
            _detectionService.SaveHomeReference(_latestFrame, _settings.Rois.RodHomeRoi, _paths.HomeReferencePath);
            _logger.Info("Home reference saved.");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Saving home reference failed");
            lblErrorStatus.Text = $"Error: {ex.Message}";
        }
    }

    private void SaveDetectionBaselineReference()
    {
        if (_latestFrame is null)
        {
            lblDetectionStatus.Text = "Detection: no frame to save";
            return;
        }

        try
        {
            _detectionService.SaveBaselineReference(_latestFrame, _paths.BaselineReferencePath);
            _logger.Info($"Detection baseline reference saved: {_paths.BaselineReferencePath}");
            lblDetectionStatus.Text = "Detection: baseline saved";
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Saving detection baseline failed");
            lblErrorStatus.Text = $"Error: {ex.Message}";
        }
    }

    private void OpenCameraPropertyDialog()
    {
        if (_settings.Camera.IsIpCamera)
        {
            MessageBox.Show(this, "Camera property controls are available for USB cameras. Use the camera web page for IP camera settings.", "DFBlackbox", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var form = new CameraSettingsForm(_cameraService, _settings.Camera.UsbCamera);
        form.ShowDialog(this);
        _settingsManager.Save(_settings);
    }

    private void MainForm_KeyDown(object? sender, KeyEventArgs e)
    {
        if (_isPlaybackMode)
        {
            if (e.KeyCode == Keys.Add)
            {
                StepPlaybackFrame(1);
                e.Handled = true;
                return;
            }

            if (e.KeyCode == Keys.Subtract)
            {
                StepPlaybackFrame(-1);
                e.Handled = true;
                return;
            }

            if (e.KeyCode == Keys.Divide)
            {
                TogglePlayback();
                e.Handled = true;
                return;
            }
        }

        if (e.KeyCode == Keys.F1)
        {
            OpenCameraPropertyDialog();
        }
    }

    private async void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_shutdownCompleted)
        {
            return;
        }

        e.Cancel = true;
        Enabled = false;
        await ShutdownAsync();
        _shutdownCompleted = true;
        Close();
    }

    private async Task ShutdownAsync()
    {
        await StopCaptureLoopAsync();
        StopRecordingIfActive(DateTime.Now);
        _settingsManager?.Save(_settings);
        ExitPlaybackMode(clearPreview: false);
        _cleanupTimer?.Dispose();
        _autoStartFullRecordingTimer?.Dispose();
        _recordingService?.Dispose();
        _detectionService.Dispose();
        _cameraService.Dispose();
        _latestFrame?.Dispose();
        picCameraPreview.Image?.Dispose();
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
    }

    private void MainForm_Resize(object? sender, EventArgs e)
    {
        if (WindowState == FormWindowState.Minimized)
        {
            HideToTray();
        }
    }

    private void HideToTray()
    {
        _initialTrayHideDone = true;
        ShowInTaskbar = false;
        Hide();
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = true;
        }
    }

    private void RestoreFromTray()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
        }
    }

    private async Task StopCaptureLoopAsync()
    {
        var cts = _captureCts;
        var task = _captureTask;
        if (cts is null)
        {
            return;
        }

        cts.Cancel();
        try
        {
            if (task is not null)
            {
                await task.ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cts.Dispose();
            if (ReferenceEquals(_captureCts, cts))
            {
                _captureCts = null;
                _captureTask = null;
            }
        }
    }

    private void DrawPrivacyMasks(Mat output)
    {
        foreach (var roi in _settings.Rois.IgnoreRois)
        {
            Cv2.Rectangle(output, ToCvRect(roi.ToRectangle()), Scalar.Black, -1);
        }
    }

    private static void DrawRoi(Mat output, RoiRect roi, Scalar color, string label, int thickness)
    {
        var rect = ToCvRect(roi.ToRectangle());
        Cv2.Rectangle(output, rect, color, thickness);
        Cv2.PutText(output, label, new OpenCvSharp.Point(rect.X + 4, Math.Max(18, rect.Y - 6)), HersheyFonts.HersheySimplex, 0.55, color, 2);
        DrawRoiHandle(output, rect.X, rect.Y, color);
        DrawRoiHandle(output, rect.X + rect.Width / 2, rect.Y, color);
        DrawRoiHandle(output, rect.X + rect.Width, rect.Y, color);
        DrawRoiHandle(output, rect.X, rect.Y + rect.Height / 2, color);
        DrawRoiHandle(output, rect.X + rect.Width, rect.Y + rect.Height / 2, color);
        DrawRoiHandle(output, rect.X, rect.Y + rect.Height, color);
        DrawRoiHandle(output, rect.X + rect.Width / 2, rect.Y + rect.Height, color);
        DrawRoiHandle(output, rect.X + rect.Width, rect.Y + rect.Height, color);
    }

    private static void DrawRoiHandle(Mat output, int x, int y, Scalar color)
    {
        const int half = 5;
        var left = Math.Clamp(x - half, 0, Math.Max(0, output.Width - 1));
        var top = Math.Clamp(y - half, 0, Math.Max(0, output.Height - 1));
        var right = Math.Clamp(x + half, 0, Math.Max(0, output.Width - 1));
        var bottom = Math.Clamp(y + half, 0, Math.Max(0, output.Height - 1));
        Cv2.Rectangle(output, new Rect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top)), color, -1);
    }

    private static OpenCvSharp.Rect ToCvRect(Rectangle rect) => new(rect.X, rect.Y, rect.Width, rect.Height);
}
