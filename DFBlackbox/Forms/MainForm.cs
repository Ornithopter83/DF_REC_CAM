using System.Diagnostics;
using DFBlackbox.Core;
using DFBlackbox.Models;
using DFBlackbox.Utils;
using Krypton.Toolkit;
using OpenCvSharp;

namespace DFBlackbox.Forms;

public sealed partial class MainForm : KryptonForm
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
    private readonly object _latestFrameSync = new();
    private DateTime _currentRecordingStartedAt;
    private string _currentTriggerReason = "";
    private double _maxMotionScore;
    private double _maxRodMotionScore;
    private double _minHomeDiffScore = double.MaxValue;
    private DateTime _lastFrameAt = DateTime.MinValue;
    private DateTime _lastDiskStatusAt = DateTime.MinValue;
    private string _cachedDiskStatus = Localization.T("Status.DiskUnknown");
    private double _cachedDiskUsedPercent;
    private long _cachedDiskFreeBytes;
    private long _cachedDiskTotalBytes;
    private DateTime _lastDashboardUpdateAt = DateTime.MinValue;
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
    private readonly object _playbackCaptureSync = new();
    private readonly object _playbackBufferSync = new();
    private readonly Queue<PlaybackFrame> _playbackBuffer = new();
    private CancellationTokenSource? _playbackPlayCts;
    private CancellationTokenSource? _playbackDecodeCts;
    private Task? _playbackPlayTask;
    private Task? _playbackDecodeTask;
    private int _playbackFrameIndex;
    private int _playbackFrameCount;
    private int _playbackDecodeNextFrameIndex;
    private double _playbackFps = 30;
    private double _playbackDurationSeconds;
    private bool _updatingPlaybackTimeline;
    private DateTime _lastPlaybackStatusUpdateAt = DateTime.MinValue;
    private bool _cameraListRefreshInProgress;
    private bool _applyingSettingsToUi;
    private bool _settingsDialogOpen;
    private readonly bool _recordingOnlyMode;
    private bool _fullScreenMode;
    private Rectangle _normalBounds;
    private FormBorderStyle _normalBorderStyle;
    private FormWindowState _normalWindowState;
    private bool _normalTopMost;
    private bool _normalSidePanelVisible;
    private bool _normalStatusStripVisible;
    private bool _normalTopHeaderVisible;
    private bool _normalPlaybackPanelVisible;
    private bool _normalVideoCameraVisible;
    private bool _normalVideoInfoVisible;
    private bool _normalVideoRecordingVisible;
    private Padding _normalMainContentPadding;
    private Padding _normalVideoPanelPadding;
    private MainWorkspaceTab _activeSideTab = MainWorkspaceTab.Recording;
    private Bitmap? _settingsGearImage;
    private FullScreenHintControl? _fullScreenHint;
    private System.Windows.Forms.Timer? _fullScreenHintTimer;
    private DateTime _fullScreenHintStartedAt;
    private VideoDropState _videoDropState = VideoDropState.None;
    private const double OverlayReferenceHeight = 1080.0;
    private const int PlaybackBufferCapacity = 10;
    private const int PlaybackStatusUpdateMilliseconds = 200;
    private static readonly HashSet<string> SupportedVideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4",
        ".avi",
        ".mov",
        ".mkv",
        ".wmv"
    };

    private enum MainWorkspaceTab
    {
        Recording,
        Playback
    }

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

    private enum VideoDropState
    {
        None,
        Accepted,
        Rejected
    }

    private sealed class PlaybackFrame : IDisposable
    {
        public int Index { get; }
        private Bitmap? Bitmap { get; set; }
        public DetectionResult Result { get; }

        public PlaybackFrame(int index, Bitmap bitmap, DetectionResult result)
        {
            Index = index;
            Bitmap = bitmap;
            Result = result;
        }

        public void Dispose()
        {
            Bitmap?.Dispose();
            Bitmap = null;
        }

        public Bitmap TakeBitmap()
        {
            Bitmap bitmap = Bitmap ?? throw new ObjectDisposedException(nameof(PlaybackFrame));
            Bitmap = null;
            return bitmap;
        }
    }

    public MainForm(bool startInTray = false, bool recordingOnlyMode = false)
    {
        _startInTray = startInTray;
        _recordingOnlyMode = recordingOnlyMode;
        InitializeComponent();
        _settingsGearImage = CreateSettingsGearImage();
        btnSettings.Values.Image = _settingsGearImage;
        btnSettings.StateCommon.Content.Image!.ImageH = PaletteRelativeAlign.Center;
        btnSettings.StateCommon.Content.Image!.ImageV = PaletteRelativeAlign.Center;
        UiTheme.ApplyFormTheme(this);
        ApplyMainVisualTheme();
        ApplyRecordingOnlyModeUi();
        InitializeFullScreenHint();
        WireEvents();
        try
        {
            InitializeApp();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Localization.T("Msg.StartFailed", ex.Message), "DFBlackbox", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ShowSideWorkspace(_activeSideTab);
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
        sidePanel.SizeChanged += (_, _) => PositionPlaybackPanel();
        btnRecordingTab.Click += (_, _) => ShowSideWorkspace(MainWorkspaceTab.Recording);
        btnPlaybackTab.Click += (_, _) => ShowSideWorkspace(MainWorkspaceTab.Playback);
        btnRefreshCamera.Click += async (_, _) => await RefreshCameraListAsync();
        mnuLanguageKor.Click += (_, _) => ChangeLanguage(Localization.Korean);
        mnuLanguageEng.Click += (_, _) => ChangeLanguage(Localization.English);
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
        btnCameraSettings.Click += (_, _) => OpenSettingsDialog("camera");
        btnRecordingSettings.Click += (_, _) => OpenSettingsDialog("recording");
        btnStorageSettings.Click += (_, _) => OpenSettingsDialog("storage");
        btnSettings.Click += (_, _) => OpenSettingsDialog("general");
        btnStartRecording.Click += async (_, _) => await StartSelectedRecordingAsync();
        btnStopRecording.Click += (_, _) => StopSelectedRecording();
        btnSaveHomeReference.Click += (_, _) => SaveDetectionBaselineReference();
        playbackControl.PreviousClicked += (_, _) => StepPlaybackFrame(-GetPlaybackSeekFrameCount());
        playbackControl.PlayPauseClicked += (_, _) => TogglePlayback();
        playbackControl.NextClicked += (_, _) => StepPlaybackFrame(GetPlaybackSeekFrameCount());
        playbackControl.SeekRequested += (_, frameIndex) => SeekPlaybackFrame(frameIndex);
        numPersonThreshold.ValueChanged += (_, _) => ReadDetectionSettingsFromUi();
        picCameraPreview.AllowDrop = true;
        picCameraPreview.DragEnter += PreviewVideo_DragEnter;
        picCameraPreview.DragOver += PreviewVideo_DragOver;
        picCameraPreview.DragLeave += PreviewVideo_DragLeave;
        picCameraPreview.DragDrop += PreviewVideo_DragDrop;
        picCameraPreview.Paint += PicCameraPreview_Paint;
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
        ApplyApplicationIcon();
        InitializeTrayIcon();
        _paths = new AppPaths(_settings.Storage);
        _paths.Ensure();
        string legacySettingsRoot = _paths.Root;
        _settingsManager = new SettingsManager(AppContext.BaseDirectory, legacySettingsRoot);
        _settings = _settingsManager.Load();
        Localization.SetLanguage(_settings.Language);
        _startInTray |= _settings.Storage.StartInTray;
        _paths = new AppPaths(_settings.Storage);
        _paths.Ensure();
        _settingsManager = new SettingsManager(AppContext.BaseDirectory, legacySettingsRoot);
        InitializeTrayIcon();
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
        ApplyLocalization();
        lblCameraStatus.Text = Localization.T("Status.CameraReady");
        lblRtspStatus.Text = _settings.Camera.IsIpCamera ? Localization.T("Status.RtspDisconnected") : Localization.T("Status.RtspNone");
        lblLastFrame.Text = Localization.T("Status.LastFrameNone");
        UpdateControlStates();
        ScheduleAutoStartFullRecording();
    }

    private void ApplyApplicationIcon()
    {
        try
        {
            using var appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (appIcon is not null)
            {
                Icon = (Icon)appIcon.Clone();
            }
        }
        catch
        {
        }
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
        _trayIcon.ContextMenuStrip.Items.Add(Localization.T("Tray.Open"), null, (_, _) => RestoreFromTray());
        _trayIcon.ContextMenuStrip.Items.Add(Localization.T("Tray.Exit"), null, (_, _) => Close());
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    private void ChangeLanguage(string language)
    {
        _settings.Language = Localization.NormalizeLanguage(language);
        Localization.SetLanguage(_settings.Language);
        _settingsManager.Save(_settings);
        InitializeTrayIcon();
        _cachedDiskStatus = Localization.T("Status.DiskUnknown");
        _lastDiskStatusAt = DateTime.MinValue;
        ApplyLocalization();
        UpdateControlStates();
        RedrawPlaybackFrameIfActive();
    }

    private void ApplyLocalization()
    {
        Text = "DFBlackbox";
        mnuLanguage.Text = Localization.T("Menu.Language");
        mnuLanguageKor.Text = Localization.T("Menu.Korean");
        mnuLanguageEng.Text = Localization.T("Menu.English");
        mnuLanguageKor.Checked = string.Equals(_settings.Language, Localization.Korean, StringComparison.OrdinalIgnoreCase);
        mnuLanguageEng.Checked = string.Equals(_settings.Language, Localization.English, StringComparison.OrdinalIgnoreCase);

        rdoIpCamera.Text = Localization.T("Main.IpCamera");
        rdoUsbCamera.Text = Localization.T("Main.UsbCamera");
        chkUseManualRtspUrl.Text = Localization.T("Main.ManualRtsp");
        btnRefreshCamera.Text = Localization.T("Main.RefreshCamera");
        btnApplyCamera.Text = Localization.T("Main.Save");
        btnWatchToggle.Text = _isWatching ? Localization.T("Main.WatchStop") : Localization.T("Main.WatchStart");
        btnDefaultSettings.Text = Localization.T("Main.Defaults");
        btnConnectCamera.Text = Localization.T("Main.Connect");
        btnDisconnectCamera.Text = Localization.T("Main.Disconnect");
        btnOpenCamera.Text = Localization.T("Main.OpenCamera");
        btnCloseCamera.Text = Localization.T("Main.CloseCamera");
        btnLoadVideoFile.Text = Localization.T("Main.LoadVideo");
        btnRecordingTab.Text = Localization.T("Main.RecordingTab");
        btnPlaybackTab.Text = Localization.T("Main.PlaybackTab");
        playbackControl.SourceText = Localization.T("Playback.Source");
        btnSettings.Text = "";
        btnSettings.AccessibleName = Localization.T("Main.Settings");
        mainToolTip.SetToolTip(btnSettings, Localization.T("Main.Settings"));
        btnCameraSettings.Text = Localization.T("Main.CameraSettings");
        btnRecordingSettings.Text = Localization.T("Main.RecordingSettings");
        btnStorageSettings.Text = Localization.T("Main.StorageSettings");
        btnOpenStorageFolder.Text = Localization.T("Main.Storage");
        rdoManualRecording.Text = Localization.T("Main.Manual");
        rdoAutoRecording.Text = Localization.T("Main.Auto");
        rdoFullRecording.Text = Localization.T("Main.Full");
        btnStartRecording.Text = Localization.T("Main.StartRecording");
        btnStopRecording.Text = Localization.T("Main.StopRecording");
        btnSaveHomeReference.Text = Localization.T("Main.SaveBaseline");
        btnCameraProperty.Text = Localization.T("Main.CameraProperty");
        chkShowPersonBox.Text = Localization.T("Main.MotionBox");
        chkShowMotionMask.Text = Localization.T("Main.MotionBox");
        chkShowRodRoi.Text = Localization.T("Main.Roi");
        chkShowHomeRoi.Text = "ROI";
        chkShowDebugText.Text = Localization.T("Main.DebugText");
        chkShowRecordingStatus.Text = Localization.T("Main.RecordingStateOverlay");

        ApplyTaggedLocalization(sideContentPanel);

        if (_fullScreenHint is not null)
        {
            _fullScreenHint.Text = Localization.T("FullScreen.Hint");
        }

        picCameraPreview.Invalidate();
        UpdateDashboardState(force: true);
    }

    private static void ApplyTaggedLocalization(Control root)
    {
        foreach (Control control in root.Controls)
        {
            if (control is Label { Tag: string key } label)
            {
                label.Text = Localization.T(key);
            }

            if (control.Controls.Count > 0)
            {
                ApplyTaggedLocalization(control);
            }
        }
    }

    private void ApplyMainVisualTheme()
    {
        topHeaderPanel.BackColor = Color.FromArgb(8, 27, 51);
        foreach (Label label in new[] { lblHeaderAppName, lblHeaderCamera, lblHeaderRecording, lblHeaderStorage })
        {
            label.BackColor = Color.Transparent;
            label.ForeColor = Color.White;
        }

        mainContentPanel.BackColor = Color.FromArgb(245, 247, 250);
        videoPanel.BackColor = Color.Black;
        foreach (Label label in new[] { lblVideoCamera, lblVideoInfo, lblVideoRecording })
        {
            label.BackColor = Color.Black;
            label.ForeColor = label == lblVideoRecording ? Color.FromArgb(255, 72, 72) : Color.White;
        }

        sidePanel.BackColor = UiTheme.PanelBack;
        sideTabHeaderPanel.BackColor = UiTheme.PanelBack;
        sideContentPanel.BackColor = UiTheme.PanelBack;
        sideContentPanel.Padding = new Padding(10, 48, 6, 8);
        playbackTabPanel.BackColor = UiTheme.PanelBack;
        playbackPanel.BackColor = Color.Transparent;
        playbackControl.BackColor = UiTheme.PanelAltBack;
        statusStrip.BackColor = UiTheme.PanelAltBack;
        menuStrip.BackColor = UiTheme.PanelBack;
        UiTheme.ApplyPrimaryKryptonButtonTheme(btnStartRecording);
        UiTheme.ApplyPrimaryKryptonButtonTheme(btnLoadVideoFile);
        UiTheme.ApplyDangerKryptonButtonTheme(btnStopRecording);
        UiTheme.ApplySecondaryKryptonButtonTheme(btnDisconnectCamera);
        UiTheme.ApplySecondaryKryptonButtonTheme(btnCloseCamera);
        UiTheme.ApplySecondaryKryptonButtonTheme(btnOpenStorageFolder);
        UiTheme.ApplySecondaryKryptonButtonTheme(btnSettings);
        UiTheme.ApplySecondaryKryptonButtonTheme(btnCameraSettings);
        UiTheme.ApplySecondaryKryptonButtonTheme(btnRecordingSettings);
        UiTheme.ApplySecondaryKryptonButtonTheme(btnStorageSettings);
        UiTheme.ApplySecondaryKryptonButtonTheme(btnSaveHomeReference);
        UiTheme.ApplySecondaryKryptonButtonTheme(btnCameraProperty);
        ApplySideTabTheme();
    }

    private void ShowSideWorkspace(MainWorkspaceTab tab)
    {
        _activeSideTab = tab;
        bool showPlayback = tab == MainWorkspaceTab.Playback;
        sideContentPanel.Visible = !showPlayback;
        playbackTabPanel.Visible = showPlayback;
        if (showPlayback)
        {
            playbackTabPanel.BringToFront();
            sideTabHeaderPanel.BringToFront();
            PositionPlaybackPanel();
        }
        else
        {
            sideContentPanel.BringToFront();
            sideTabHeaderPanel.BringToFront();
        }

        ApplySideTabTheme();
    }

    private void ApplySideTabTheme()
    {
        if (_activeSideTab == MainWorkspaceTab.Recording)
        {
            UiTheme.ApplyPrimaryKryptonButtonTheme(btnRecordingTab);
            UiTheme.ApplySecondaryKryptonButtonTheme(btnPlaybackTab);
        }
        else
        {
            UiTheme.ApplySecondaryKryptonButtonTheme(btnRecordingTab);
            UiTheme.ApplyPrimaryKryptonButtonTheme(btnPlaybackTab);
        }
    }

    private static Bitmap CreateSettingsGearImage()
    {
        var bitmap = new Bitmap(24, 24, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        const int toothCount = 8;
        const float outerRadius = 10F;
        const float rootRadius = 7.5F;
        const float centerX = 12F;
        const float centerY = 12F;
        var points = new List<PointF>(toothCount * 4);
        double step = Math.PI * 2 / toothCount;
        for (int tooth = 0; tooth < toothCount; tooth++)
        {
            double centerAngle = -Math.PI / 2 + tooth * step;
            AddGearPoint(points, centerX, centerY, rootRadius, centerAngle - step * 0.43);
            AddGearPoint(points, centerX, centerY, outerRadius, centerAngle - step * 0.22);
            AddGearPoint(points, centerX, centerY, outerRadius, centerAngle + step * 0.22);
            AddGearPoint(points, centerX, centerY, rootRadius, centerAngle + step * 0.43);
        }

        using var path = new System.Drawing.Drawing2D.GraphicsPath(System.Drawing.Drawing2D.FillMode.Alternate);
        path.AddPolygon(points.ToArray());
        path.AddEllipse(centerX - 2.8F, centerY - 2.8F, 5.6F, 5.6F);
        using var brush = new SolidBrush(UiTheme.AccentDark);
        graphics.FillPath(brush, path);
        return bitmap;
    }

    private static void AddGearPoint(List<PointF> points, float centerX, float centerY, float radius, double angle)
    {
        points.Add(new PointF(
            centerX + radius * (float)Math.Cos(angle),
            centerY + radius * (float)Math.Sin(angle)));
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
        if (_recordingOnlyMode
            && string.Equals(_settings.Recording.Mode, "Auto", StringComparison.OrdinalIgnoreCase))
        {
            rdoManualRecording.Checked = true;
            return;
        }

        string mode = _settings.Recording.Mode;
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

    private void ApplyRecordingOnlyModeUi()
    {
        if (!_recordingOnlyMode)
        {
            return;
        }

        btnSaveHomeReference.Visible = false;
        btnWatchToggle.Visible = false;
        rdoAutoRecording.Visible = false;
        if (rdoAutoRecording.Checked)
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
            lblCameraStatus.Text = Localization.T("Status.CameraUsbScanning");
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
            lblErrorStatus.Text = Localization.T("Status.Error", $"USB camera scan failed ({ex.Message})");
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
                lblCameraStatus.Text = Localization.T("Status.CameraUsbNone");
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
            lblCameraStatus.Text = Localization.T("Status.SettingsSaved");
            MessageBox.Show(this, Localization.T("Msg.SettingsSaved"), "DFBlackbox", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Saving settings failed");
            lblErrorStatus.Text = Localization.T("Status.Error", ex.Message);
            MessageBox.Show(this, Localization.T("Msg.SettingsSaveFailed", ex.Message), "DFBlackbox", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ResetDefaults()
    {
        if (_recordingService?.IsRecording == true)
        {
            MessageBox.Show(this, Localization.T("Msg.StopRecordingBeforeDefaults"), "DFBlackbox", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string currentLanguage = _settings.Language;
        _settings = new AppSettings { Language = currentLanguage };
        _stateMachine = new BlackboxStateMachine(_settings) { AutoRecordingEnabled = false };
        _detectionService.ResetBackground();
        _detectionService.LoadBaselineReference(_paths.BaselineReferencePath);
        ApplySettingsToUi();
        _settingsManager.Save(_settings);
        Localization.SetLanguage(_settings.Language);
        ApplyLocalization();
        lblCameraStatus.Text = Localization.T("Status.DefaultsRestored");
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
            lblDetectionStatus.Text = File.Exists(_paths.BaselineReferencePath) ? Localization.T("Status.DetectionWatching") : Localization.T("Status.DetectionNoBaseline");
        }
        else
        {
            lblDetectionStatus.Text = Localization.T("Status.DetectionManual");
        }

        btnWatchToggle.Text = _isWatching ? Localization.T("Main.WatchStop") : Localization.T("Main.WatchStart");
        _stateMachine.AutoRecordingEnabled = _isWatching && rdoAutoRecording.Checked;
        UpdateControlStates();
    }

    private async Task ConnectCameraAsync()
    {
        ExitPlaybackMode(clearPreview: true);
        if (_recordingService?.IsRecording == true)
        {
            MessageBox.Show(this, Localization.T("Msg.StopRecordingBeforeCameraChange"), "DFBlackbox", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        await StopCaptureLoopAsync();
        ReadCameraSettingsFromUi();
        if (!_settings.Camera.IsIpCamera && !await EnsureUsbCameraSelectedAsync())
        {
            lblCameraStatus.Text = Localization.T("Status.CameraUsbNone");
            UpdateControlStates();
            return;
        }

        ReadDetectionSettingsFromUi();
        _settingsManager.Save(_settings);
        btnConnectCamera.Enabled = false;
        lblCameraStatus.Text = Localization.T("Status.CameraConnecting");
        lblRtspStatus.Text = _settings.Camera.IsIpCamera ? Localization.T("Status.RtspConnecting") : Localization.T("Status.RtspNone");
        bool opened = await Task.Run(() => _cameraService.Open(_settings));
        btnConnectCamera.Enabled = true;
        _lastFrameAt = DateTime.Now;
        _consecutiveFrameFailures = 0;
        lblCameraStatus.Text = opened ? GetCameraStatusText() : GetCameraErrorText();
        lblRtspStatus.Text = _settings.Camera.IsIpCamera ? $"RTSP: {_cameraService.ConnectionState}" : Localization.T("Status.RtspNone");
        UpdateControlStates();
    }

    private async Task DisconnectCameraAsync()
    {
        await CloseCameraPreviewAsync();
        StopRecordingIfActive(DateTime.Now);
        _cameraService.Close();
        SetWatching(false);
        lblCameraStatus.Text = Localization.T("Status.CameraDisconnected");
        lblRtspStatus.Text = _settings.Camera.IsIpCamera ? Localization.T("Status.RtspClosed") : Localization.T("Status.RtspNone");
        lblLastFrame.Text = Localization.T("Status.LastFrameNone");
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
        Image? old = picCameraPreview.Image;
        picCameraPreview.Image = null;
        old?.Dispose();
        lblCameraStatus.Text = _cameraService.IsOpened ? $"{GetCameraStatusText()} / closed" : Localization.T("Status.CameraClosed");
        UpdateControlStates();
    }

    private async Task LoadVideoFileAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Title = Localization.T("Main.LoadRecordingTitle"),
            Filter = Localization.T("Main.VideoFileFilter"),
            InitialDirectory = Directory.Exists(_paths.RecVideos) ? _paths.RecVideos : AppContext.BaseDirectory
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        await OpenPlaybackFileAsync(dialog.FileName);
    }

    private void PreviewVideo_DragEnter(object? sender, DragEventArgs e)
    {
        UpdateVideoDragState(e);
    }

    private void PreviewVideo_DragOver(object? sender, DragEventArgs e)
    {
        UpdateVideoDragState(e);
    }

    private void PreviewVideo_DragLeave(object? sender, EventArgs e)
    {
        SetVideoDropVisualState(VideoDropState.None);
        picCameraPreview.Update();
    }

    private async void PreviewVideo_DragDrop(object? sender, DragEventArgs e)
    {
        try
        {
            bool supported = TryGetSingleDroppedVideoFile(e.Data, out string filePath);
            e.Effect = supported ? DragDropEffects.Copy : DragDropEffects.None;
            SetVideoDropVisualState(VideoDropState.None);
            picCameraPreview.Update();
            if (!supported)
            {
                lblCameraStatus.Text = Localization.T("Status.VideoDropRejected");
                return;
            }

            await OpenPlaybackFileAsync(filePath);
        }
        catch (Exception ex)
        {
            SetVideoDropVisualState(VideoDropState.None);
            _logger?.Error(ex, "Video drag-and-drop playback failed");
            lblErrorStatus.Text = Localization.T("Status.Error", ex.Message);
        }
    }

    private void UpdateVideoDragState(DragEventArgs e)
    {
        VideoDropState state = TryGetSingleDroppedVideoFile(e.Data, out _)
            ? VideoDropState.Accepted
            : VideoDropState.Rejected;
        e.Effect = state == VideoDropState.Accepted ? DragDropEffects.Copy : DragDropEffects.None;
        SetVideoDropVisualState(state);
    }

    private static bool TryGetSingleDroppedVideoFile(IDataObject? data, out string filePath)
    {
        filePath = "";
        if (data is null || !data.GetDataPresent(DataFormats.FileDrop))
        {
            return false;
        }

        if (data.GetData(DataFormats.FileDrop) is not string[] files || files.Length != 1)
        {
            return false;
        }

        string candidate = files[0];
        if (string.IsNullOrWhiteSpace(candidate) || !File.Exists(candidate) || !IsSupportedVideoFile(candidate))
        {
            return false;
        }

        filePath = candidate;
        return true;
    }

    private static bool IsSupportedVideoFile(string filePath)
    {
        return SupportedVideoExtensions.Contains(Path.GetExtension(filePath));
    }

    private void SetVideoDropVisualState(VideoDropState state)
    {
        _videoDropState = state;
        Cursor cursor = state switch
        {
            VideoDropState.Accepted => Cursors.Hand,
            VideoDropState.Rejected => Cursors.No,
            _ => Cursors.Default
        };
        picCameraPreview.Cursor = cursor;
        picCameraPreview.Invalidate();
    }

    private void PicCameraPreview_Paint(object? sender, PaintEventArgs e)
    {
        if (_videoDropState == VideoDropState.None)
        {
            return;
        }

        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        bool accepted = _videoDropState == VideoDropState.Accepted;
        Color accent = accepted ? Color.FromArgb(30, 170, 95) : Color.FromArgb(210, 55, 65);
        string text = accepted
            ? Localization.T("Main.VideoDropReady")
            : Localization.T("Main.VideoDropRejected");
        using var fill = new SolidBrush(Color.FromArgb(92, accent));
        using var border = new Pen(Color.FromArgb(235, accent), 6F);
        using var shadow = new SolidBrush(Color.FromArgb(190, Color.Black));
        using var foreground = new SolidBrush(Color.White);
        using var font = new Font("Malgun Gothic", 24F, FontStyle.Bold);

        Rectangle bounds = picCameraPreview.ClientRectangle;
        e.Graphics.FillRectangle(fill, bounds);
        Rectangle borderRect = Rectangle.Inflate(bounds, -8, -8);
        e.Graphics.DrawRectangle(border, borderRect);

        SizeF textSize = e.Graphics.MeasureString(text, font);
        float x = (bounds.Width - textSize.Width) / 2F;
        float y = (bounds.Height - textSize.Height) / 2F;
        e.Graphics.DrawString(text, font, shadow, x + 2, y + 2);
        e.Graphics.DrawString(text, font, foreground, x, y);
    }

    private async Task OpenPlaybackFileAsync(string filePath)
    {
        await EnterPlaybackModeAsync(filePath);
    }

    private async Task EnterPlaybackModeAsync(string filePath)
    {
        await DisconnectCameraAsync();
        ExitPlaybackMode(clearPreview: false);

        VideoCapture capture = OpenPlaybackCapture(filePath);
        if (!capture.IsOpened())
        {
            capture.Dispose();
            MessageBox.Show(this, Localization.T("Msg.CouldNotOpenVideo"), "DFBlackbox", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _playbackCapture = capture;
        _playbackPath = filePath;
        _playbackFrameIndex = 0;
        _playbackFrameCount = Math.Max(0, (int)Math.Round(capture.Get(VideoCaptureProperties.FrameCount)));
        _playbackDurationSeconds = TryReadMp4DurationSeconds(filePath);
        _playbackFps = DetectPlaybackFps(capture, filePath, _playbackFrameCount);
        _fpsCounter.Reset();

        _isPlaybackMode = true;
        _playbackPlaying = false;
        ConfigurePlaybackTimeline();
        playbackControl.FpsText = $"FPS: {_playbackFps:0.#}";
        playbackPanel.Visible = true;
        ShowSideWorkspace(MainWorkspaceTab.Playback);
        PositionPlaybackPanel();
        playbackPanel.BringToFront();
        playbackControl.IsPlaying = false;
        playbackControl.Value = 0;
        lblCameraStatus.Text = Localization.T("Status.PlaybackLoaded");
        lblRtspStatus.Text = Localization.T("Status.File", Path.GetFileName(filePath));
        ShowPlaybackFrame(0);
        UpdateControlStates();
    }

    private VideoCapture OpenPlaybackCapture(string filePath)
    {
        var capture = new VideoCapture();
        try
        {
            // OpenCV 하드웨어 가속 옵션을 우선 요청한다.
            // 백엔드별 지원 여부가 달라 실패해도 무시하고 소프트웨어 디코딩으로 이어간다.
            capture.Set((VideoCaptureProperties)50, 1);
            capture.Set((VideoCaptureProperties)52, 1);
        }
        catch
        {
        }

        capture.Open(filePath);
        return capture;
    }

    private void ExitPlaybackMode(bool clearPreview)
    {
        StopPlaybackLoop();
        ReleasePlaybackModeResources(clearPreview);
    }

    private async Task ExitPlaybackModeAsync(bool clearPreview)
    {
        await StopPlaybackLoopAsync();
        ReleasePlaybackModeResources(clearPreview);
    }

    private void ReleasePlaybackModeResources(bool clearPreview)
    {
        lock (_playbackCaptureSync)
        {
            _playbackCapture?.Dispose();
            _playbackCapture = null;
        }

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
            Image? old = picCameraPreview.Image;
            picCameraPreview.Image = null;
            old?.Dispose();
        }

        UpdateControlStates();
    }

    private double DetectPlaybackFps(VideoCapture capture, string filePath, int frameCount)
    {
        double metadataFps = capture.Get(VideoCaptureProperties.Fps);
        double mp4DurationSeconds = _playbackDurationSeconds > 0 ? _playbackDurationSeconds : TryReadMp4DurationSeconds(filePath);
        // OpenCV가 보고하는 FPS 메타데이터가 부정확한 파일이 있어, 우선 MP4 duration / 프레임 수로 평균 FPS를 재계산한다.
        // 두 값이 10% 이상 다르면 재생 속도 체감이 달라지므로 duration 기반 값을 우선한다.
        double durationFps = frameCount > 1 && mp4DurationSeconds > 0
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

        double originalFrame = capture.Get(VideoCaptureProperties.PosFrames);
        try
        {
            capture.Set(VideoCaptureProperties.PosFrames, frameCount - 1);
            using var frame = new Mat();
            if (!capture.Read(frame) || frame.Empty())
            {
                return 0;
            }

            double lastFrameMs = capture.Get(VideoCaptureProperties.PosMsec);
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
        // 외부 ffprobe 없이 MP4 컨테이너의 moov/mvhd atom만 읽어 전체 길이를 얻는다.
        // 재생 FPS 계산용이므로 파싱 실패 시 0을 반환하고 OpenCV fallback으로 넘어간다.
        while (stream.Position + 8 <= endOffset)
        {
            long atomStart = stream.Position;
            uint atomSize = ReadUInt32BigEndian(stream);
            string atomType = ReadAscii(stream, 4);
            long headerSize = 8;
            if (atomSize == 1)
            {
                if (stream.Position + 8 > endOffset)
                {
                    return 0;
                }

                atomSize = 0;
                ulong extendedSize = ReadUInt64BigEndian(stream);
                headerSize = 16;
                if (extendedSize > long.MaxValue)
                {
                    return 0;
                }

                atomSize = (uint)Math.Min(uint.MaxValue, extendedSize);
                long atomEnd = atomStart + (long)extendedSize;
                double duration = ReadMp4AtomPayload(stream, atomType, atomEnd);
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

            long currentAtomEnd = atomSize == 0 ? endOffset : Math.Min(atomStart + atomSize, endOffset);
            double found = ReadMp4AtomPayload(stream, atomType, currentAtomEnd);
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

        int version = stream.ReadByte();
        stream.Position += 3;
        if (version == 1)
        {
            if (stream.Position + 28 > atomEnd)
            {
                return 0;
            }

            stream.Position += 16;
            uint timescale = ReadUInt32BigEndian(stream);
            ulong duration = ReadUInt64BigEndian(stream);
            return timescale > 0 ? duration / (double)timescale : 0;
        }

        if (stream.Position + 16 > atomEnd)
        {
            return 0;
        }

        stream.Position += 8;
        uint scale = ReadUInt32BigEndian(stream);
        uint duration32 = ReadUInt32BigEndian(stream);
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
        _playbackPlayTask = RunPlaybackLoopAsync(_playbackPlayCts.Token);
    }

    private void StopPlaybackLoop()
    {
        CancellationTokenSource? playCts = _playbackPlayCts;
        CancellationTokenSource? decodeCts = _playbackDecodeCts;
        Task? playTask = _playbackPlayTask;
        Task? decodeTask = _playbackDecodeTask;

        playCts?.Cancel();
        decodeCts?.Cancel();
        _playbackPlayCts = null;
        _playbackDecodeCts = null;
        _playbackPlayTask = null;
        _playbackDecodeTask = null;
        _ = ObserveStoppedPlaybackLoopAsync(playTask, decodeTask, playCts, decodeCts);
        ClearPlaybackBuffer();
    }

    private async Task StopPlaybackLoopAsync()
    {
        CancellationTokenSource? playCts = _playbackPlayCts;
        CancellationTokenSource? decodeCts = _playbackDecodeCts;
        Task? playTask = _playbackPlayTask;
        Task? decodeTask = _playbackDecodeTask;

        playCts?.Cancel();
        decodeCts?.Cancel();
        _playbackPlayCts = null;
        _playbackDecodeCts = null;
        _playbackPlayTask = null;
        _playbackDecodeTask = null;

        await AwaitPlaybackTaskCompletionAsync(decodeTask);
        await AwaitPlaybackTaskCompletionAsync(playTask);
        decodeCts?.Dispose();
        playCts?.Dispose();
        ClearPlaybackBuffer();
    }

    private async Task ObserveStoppedPlaybackLoopAsync(
        Task? playTask,
        Task? decodeTask,
        CancellationTokenSource? playCts,
        CancellationTokenSource? decodeCts)
    {
        await AwaitPlaybackTaskCompletionAsync(decodeTask);
        await AwaitPlaybackTaskCompletionAsync(playTask);
        decodeCts?.Dispose();
        playCts?.Dispose();
    }

    private async Task AwaitPlaybackTaskCompletionAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Playback loop stopped with error");
        }
    }

    private async Task RunPlaybackLoopAsync(CancellationToken token)
    {
        if (UseBufferedPlayback())
        {
            await RunBufferedPlaybackLoopAsync(token);
            return;
        }

        try
        {
            double intervalMs = Math.Clamp(1000.0 / Math.Max(1, _playbackFps), 1, 200);
            var stopwatch = Stopwatch.StartNew();
            double nextFrameAtMs = 0.0;

            while (!token.IsCancellationRequested && _isPlaybackMode && _playbackPlaying)
            {
                PlayNextPlaybackFrame();
                if (!_playbackPlaying)
                {
                    break;
                }

                nextFrameAtMs += intervalMs;
                double delayMs = nextFrameAtMs - stopwatch.Elapsed.TotalMilliseconds;
                // WinForms Timer 대신 Stopwatch 기준 누적 시각을 맞춰 장시간 재생 시 드리프트를 줄인다.
                // 처리 시간이 프레임 간격보다 길어도 최소 대기 시간을 둬 WM_PAINT가 화면을 갱신할 틈을 준다.
                if (delayMs > 1)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(delayMs), token);
                }
                else
                {
                    await Task.Delay(1, token);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private bool UseBufferedPlayback()
    {
        return _isPlaybackMode
            && !IsPlaybackTrackingFirst()
            && (!ShouldDrawPlaybackOverlay() || IsPlaybackFirst());
    }

    private bool IsPlaybackFirst()
    {
        return string.Equals(_settings.Overlay.PlaybackOptimizationMode, "Playback", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsPlaybackTrackingFirst()
    {
        return string.Equals(_settings.Overlay.PlaybackOptimizationMode, "Tracking", StringComparison.OrdinalIgnoreCase);
    }

    private async Task RunBufferedPlaybackLoopAsync(CancellationToken token)
    {
        _playbackDecodeCts?.Cancel();
        _playbackDecodeCts?.Dispose();
        _playbackDecodeCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        _playbackDecodeNextFrameIndex = _playbackFrameIndex + 1;
        _playbackDecodeTask = Task.Run(() => FillPlaybackBufferAsync(_playbackDecodeCts.Token), _playbackDecodeCts.Token);

        try
        {
            double intervalMs = Math.Clamp(1000.0 / Math.Max(1, _playbackFps), 1, 200);
            var stopwatch = Stopwatch.StartNew();
            int startIndex = _playbackFrameIndex;

            while (!token.IsCancellationRequested && _isPlaybackMode && _playbackPlaying)
            {
                int targetIndex = startIndex + Math.Max(1, (int)Math.Floor(stopwatch.Elapsed.TotalMilliseconds / intervalMs));
                if (_playbackFrameCount > 0)
                {
                    targetIndex = Math.Min(targetIndex, _playbackFrameCount - 1);
                }

                PlaybackFrame? frame = DequeuePlaybackFrame(targetIndex);
                if (frame is null)
                {
                    if (_playbackFrameCount > 0 && _playbackFrameIndex >= _playbackFrameCount - 1)
                    {
                        _playbackPlaying = false;
                        playbackControl.IsPlaying = false;
                        break;
                    }

                    await Task.Delay(1, token);
                    continue;
                }

                using (frame)
                {
                    ApplyBufferedPlaybackFrame(frame);
                }

                if (_playbackFrameCount > 0 && _playbackFrameIndex >= _playbackFrameCount - 1)
                {
                    _playbackPlaying = false;
                    playbackControl.IsPlaying = false;
                    break;
                }

                await Task.Delay(1, token);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task FillPlaybackBufferAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (_playbackFrameCount > 0 && _playbackDecodeNextFrameIndex >= _playbackFrameCount)
                {
                    return;
                }

                if (GetPlaybackBufferCount() >= PlaybackBufferCapacity)
                {
                    await Task.Delay(1, token);
                    continue;
                }

                PlaybackFrame? frame = ReadBufferedPlaybackFrame(_playbackDecodeNextFrameIndex);
                if (frame is null)
                {
                    return;
                }

                lock (_playbackBufferSync)
                {
                    if (_playbackBuffer.Count < PlaybackBufferCapacity)
                    {
                        _playbackBuffer.Enqueue(frame);
                        _playbackDecodeNextFrameIndex = frame.Index + 1;
                    }
                    else
                    {
                        frame.Dispose();
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Buffered playback decode failed");
        }
    }

    private PlaybackFrame? ReadBufferedPlaybackFrame(int frameIndex)
    {
        if (_playbackCapture is null)
        {
            return null;
        }

        using var capturedFrame = new Mat();
        bool read;
        lock (_playbackCaptureSync)
        {
            if (_playbackCapture is null)
            {
                return null;
            }

            read = _playbackCapture.Read(capturedFrame);
        }

        if (!read || capturedFrame.Empty())
        {
            return null;
        }

        DetectionResult result = CreateBypassDetectionResult(DateTime.Now);
        Bitmap bitmap = BitmapConverter.ToBitmap(capturedFrame);
        return new PlaybackFrame(frameIndex, bitmap, result);
    }

    private int GetPlaybackBufferCount()
    {
        lock (_playbackBufferSync)
        {
            return _playbackBuffer.Count;
        }
    }

    private PlaybackFrame? DequeuePlaybackFrame(int targetIndex)
    {
        lock (_playbackBufferSync)
        {
            PlaybackFrame? selected = null;
            while (_playbackBuffer.Count > 0 && _playbackBuffer.Peek().Index <= targetIndex)
            {
                selected?.Dispose();
                selected = _playbackBuffer.Dequeue();
            }

            return selected;
        }
    }

    private void ClearPlaybackBuffer()
    {
        lock (_playbackBufferSync)
        {
            while (_playbackBuffer.Count > 0)
            {
                _playbackBuffer.Dequeue().Dispose();
            }
        }
    }

    private void ApplyBufferedPlaybackFrame(PlaybackFrame frame)
    {
        _playbackFrameIndex = frame.Index;
        _lastFrameAt = DateTime.Now;
        _fpsCounter.Tick();
        Image? old = picCameraPreview.Image;
        picCameraPreview.Image = frame.TakeBitmap();
        old?.Dispose();
        UpdatePlaybackInfoThrottled(force: _playbackFrameIndex >= Math.Max(0, _playbackFrameCount - 1));
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
            // 사용자가 드래그/스텝 이동할 때만 명시 seek를 수행한다.
            // 일반 재생 중에는 순차 Read를 유지해야 일부 코덱에서 프레임 건너뜀과 속도 저하가 적다.
            ClearPlaybackBuffer();
            lock (_playbackCaptureSync)
            {
                _playbackCapture?.Set(VideoCaptureProperties.PosFrames, frameIndex);
            }
        }

        using var capturedFrame = new Mat();
        bool read;
        lock (_playbackCaptureSync)
        {
            if (_playbackCapture is null)
            {
                return;
            }

            read = _playbackCapture.Read(capturedFrame);
        }

        if (!read || capturedFrame.Empty())
        {
            _playbackPlaying = false;
            StopPlaybackLoop();
            playbackControl.IsPlaying = false;
            return;
        }

        _playbackFrameIndex = frameIndex;
        _lastFrameAt = DateTime.Now;
        var result = ShouldRunPlaybackAlgorithm()
            ? AnalyzeFrame(capturedFrame, DateTime.Now)
            : CreateBypassDetectionResult(DateTime.Now);
        if (ShouldDrawPlaybackOverlay())
        {
            using var preview = DrawOverlay(capturedFrame, result);
            UpdatePreview(preview, result);
        }
        else
        {
            UpdatePreview(capturedFrame, result);
        }

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
        return !IsPlaybackFirst() && ShouldDrawPlaybackOverlay();
    }

    private bool ShouldDrawPlaybackOverlay()
    {
        if (IsPlaybackFirst())
        {
            return false;
        }

        return (_settings.Overlay.ShowRodRoi && _settings.Overlay.ShowPlaybackRoiOutlines)
            || (_settings.Overlay.ShowRodRoi && _settings.Overlay.ShowPlaybackDiffMessage)
            || (_settings.Overlay.ShowRodRoi && _settings.Overlay.ShowPlaybackTrackingCandidate)
            || _settings.Overlay.ShowDebugText;
    }

    private void UpdatePlaybackInfo()
    {
        string total = _playbackFrameCount > 0 ? _playbackFrameCount.ToString() : "?";
        double time = _playbackFps > 0 ? _playbackFrameIndex / _playbackFps : 0;
        double totalSeconds = _playbackDurationSeconds > 0
            ? _playbackDurationSeconds
            : _playbackFps > 0 && _playbackFrameCount > 0
                ? _playbackFrameCount / _playbackFps
                : 0;
        playbackControl.SetPosition(_playbackFrameIndex, FormatPlaybackTime(time), FormatPlaybackTime(totalSeconds));
        playbackControl.FpsText = $"FPS: {_playbackFps:0.#}";
        lblRtspStatus.Text = $"File: {Path.GetFileName(_playbackPath)}  Frame {_playbackFrameIndex + 1}/{total}";
        lblLastFrame.Text = Localization.T("Status.Frame", _playbackFrameCount > 0 ? $"{_playbackFrameIndex + 1}/{_playbackFrameCount}" : $"{_playbackFrameIndex + 1}");
        lblFps.Text = Localization.T("Status.Fps", _fpsCounter.CurrentFps);
    }

    private int GetPlaybackSeekFrameCount() => Math.Max(1, (int)Math.Round(_playbackFps * 5));

    private void UpdatePlaybackInfoThrottled(bool force = false)
    {
        DateTime now = DateTime.Now;
        if (!force && now - _lastPlaybackStatusUpdateAt < TimeSpan.FromMilliseconds(PlaybackStatusUpdateMilliseconds))
        {
            return;
        }

        _lastPlaybackStatusUpdateAt = now;
        UpdatePlaybackInfo();
        UpdatePlaybackTimeline();
        lblFps.Text = Localization.T("Status.Fps", _fpsCounter.CurrentFps);
        lblCameraStatus.Text = _playbackPlaying ? Localization.T("Status.PlaybackPlaying") : Localization.T("Status.PlaybackPaused");
        lblRecordingStatus.Text = Localization.T("Status.RecordingPlayback");
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

        TimeSpan time = TimeSpan.FromSeconds(seconds);
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
                    SetLatestFrame(frame);
                    DateTime now = DateTime.Now;
                    _stateMachine.AutoRecordingEnabled = _isWatching && rdoAutoRecording.Checked;
                    bool algorithmEnabled = _isWatching && ShouldRunAlgorithm();
                    var result = algorithmEnabled
                        ? AnalyzeFrame(frame, now)
                        : CreateBypassDetectionResult(now);
                    var stateResult = algorithmEnabled
                        ? _stateMachine.Update(result, now)
                        : new StateUpdateResult { NewState = _stateMachine.CurrentState };
                    // 녹화가 아직 시작되지 않았더라도 감시/수동 요청 중이면 사전 버퍼를 채운다.
                    // 실제 트리거가 발생하면 이 버퍼가 녹화 파일 앞부분에 먼저 기록된다.
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
                        ShowRecordingStamp(Localization.T("Stamp.RecordingStarted"), Color.FromArgb(24, 132, 74));
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
                        string filePath = _recordingService.StopRecording(now);
                        ShowRecordingStamp(Localization.T("Stamp.RecordingStopped"), Color.FromArgb(192, 92, 24));
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
                BeginInvoke(() => lblErrorStatus.Text = Localization.T("Status.Error", ex.Message));
                await Task.Delay(500, token);
            }
        }
    }

    private async Task HandleNoFrameAsync(CancellationToken token)
    {
        TimeSpan timeout = TimeSpan.FromSeconds(_settings.Camera.NoFrameTimeoutSeconds);
        if (_consecutiveFrameFailures >= 10 || (_lastFrameAt != DateTime.MinValue && DateTime.Now - _lastFrameAt > timeout))
        {
            _cameraService.MarkReconnecting();
            BeginInvoke(() =>
            {
                lblCameraStatus.Text = Localization.T("Status.CameraReconnecting");
                lblRtspStatus.Text = _settings.Camera.IsIpCamera ? Localization.T("Status.RtspReconnecting") : Localization.T("Status.RtspNone");
            });
            _cameraService.Close();
            await Task.Delay(TimeSpan.FromSeconds(_settings.Camera.ReconnectDelaySeconds), token);
            bool opened = _cameraService.Open(_settings);
            BeginInvoke(() =>
            {
                lblCameraStatus.Text = opened ? GetCameraStatusText() : GetCameraErrorText();
                lblRtspStatus.Text = _settings.Camera.IsIpCamera ? $"RTSP: {_cameraService.ConnectionState}" : Localization.T("Status.RtspNone");
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
        int sourceWidth = Math.Max(1, frame.Width);
        int sourceHeight = Math.Max(1, frame.Height);
        var output = CreateOverlayCanvas(frame);
        DrawPrivacyMasks(output);
        if (_settings.Overlay.ShowRodRoi && (!_isPlaybackMode || _settings.Overlay.ShowPlaybackRoiOutlines))
        {
            DrawRoi(output, ScaleRoiForFrame(_settings.Rois.PersonWatchRoi, output), Scalar.Yellow, "ROI", 2);
            DrawRoi(output, ScaleRoiForFrame(_settings.Rois.IgnoreRoi, output), new Scalar(180, 180, 180), "IGNORE_ROI", 2);
        }

        if (_settings.Overlay.ShowMotionMask)
        {
            foreach (var box in result.MotionBoxes)
            {
                Cv2.Rectangle(output, ToCvRect(ScaleRectToOverlay(box, sourceWidth, sourceHeight, output)), Scalar.Orange, OverlayThickness(output, 2));
            }
        }

        if (_settings.Overlay.ShowPersonBox)
        {
            foreach (var box in result.PersonCandidateBoxes)
            {
                Cv2.Rectangle(output, ToCvRect(ScaleRectToOverlay(box, sourceWidth, sourceHeight, output)), Scalar.Blue, OverlayThickness(output, 2));
            }
        }

        if (_recordingService.IsRecording && _settings.Overlay.ShowRecordingStatus)
        {
            Cv2.PutText(output, "REC", OverlayPoint(output, 24, 42), HersheyFonts.HersheySimplex, OverlayFontScale(output, 1.2), Scalar.Red, OverlayThickness(output, 3));
        }

        if ((_isPlaybackMode && _settings.Overlay.ShowPlaybackTrackingCandidate) || (!_isPlaybackMode && _recordingService.IsRecording))
        {
            DrawTrackingCandidateOverlay(output, result, sourceWidth, sourceHeight);
        }

        if (_isPlaybackMode && _settings.Overlay.ShowPlaybackDiffMessage)
        {
            DrawPlaybackDiffOverlay(output, result);
        }

        if (_recordingService.IsRecording && stateResult is not null && _settings.Overlay.ShowDebugText)
        {
            DrawRecordingHoldOverlay(output, result, stateResult);
        }

        if (_settings.Overlay.ShowDebugText)
        {
            Cv2.PutText(output, $"{_stateMachine.CurrentState} {result.DebugText}", OverlayBottomLeftPoint(output, 24, 24), HersheyFonts.HersheySimplex, OverlayFontScale(output, 0.6), Scalar.White, OverlayThickness(output, 2));
        }

        return output;
    }

    private void DrawRecordingHoldOverlay(Mat output, DetectionResult result, StateUpdateResult stateResult)
    {
        string reason = string.IsNullOrWhiteSpace(stateResult.RecordingHoldReason)
            ? "KEEP: unknown"
            : stateResult.RecordingHoldReason;
        double scale = OverlayScale(output);
        int y = Math.Max(ScaleOverlayLength(64, scale), output.Height - ScaleOverlayLength(64, scale));
        Cv2.Rectangle(output, new OpenCvSharp.Rect(
            ScaleOverlayLength(18, scale),
            y - ScaleOverlayLength(34, scale),
            Math.Min(output.Width - ScaleOverlayLength(36, scale), ScaleOverlayLength(760, scale)),
            ScaleOverlayLength(44, scale)), Scalar.Black, -1);
        Cv2.PutText(output, reason, new OpenCvSharp.Point(ScaleOverlayLength(28, scale), y), HersheyFonts.HersheySimplex, OverlayFontScale(output, 0.8), Scalar.Cyan, OverlayThickness(output, 2));
    }

    private void DrawPlaybackDiffOverlay(Mat output, DetectionResult result)
    {
        string text = $"ROI_Diff {result.PersonMotionScore:0.000} / th {_settings.Detection.PersonMotionRatioThreshold:0.000}";
        double scale = OverlayScale(output);
        int y = Math.Max(ScaleOverlayLength(64, scale), output.Height - ScaleOverlayLength(64, scale));
        Cv2.Rectangle(output, new OpenCvSharp.Rect(
            ScaleOverlayLength(18, scale),
            y - ScaleOverlayLength(34, scale),
            Math.Min(output.Width - ScaleOverlayLength(36, scale), ScaleOverlayLength(520, scale)),
            ScaleOverlayLength(44, scale)), Scalar.Black, -1);
        Cv2.PutText(output, text, new OpenCvSharp.Point(ScaleOverlayLength(28, scale), y), HersheyFonts.HersheySimplex, OverlayFontScale(output, 0.8), Scalar.Cyan, OverlayThickness(output, 2));
    }

    private void DrawTrackingCandidateOverlay(Mat output, DetectionResult result, int sourceWidth, int sourceHeight)
    {
        var candidate = FindLargestRecordingHoldCandidate(result, sourceWidth, sourceHeight);
        if (candidate.HasValue)
        {
            var overlayCandidate = ScaleRectToOverlay(candidate.Value, sourceWidth, sourceHeight, output);
            var candidateColor = result.PersonMotionScore >= _settings.Detection.PersonMotionRatioThreshold
                ? Scalar.Red
                : Scalar.Cyan;
            Cv2.Rectangle(output, ToCvRect(overlayCandidate), candidateColor, OverlayThickness(output, 4));
            Cv2.PutText(
                output,
                "KEEP CANDIDATE",
                new OpenCvSharp.Point(overlayCandidate.X + OverlayLength(output, 6), Math.Max(OverlayLength(output, 22), overlayCandidate.Y - OverlayLength(output, 8))),
                HersheyFonts.HersheySimplex,
                OverlayFontScale(output, 0.7),
                candidateColor,
                OverlayThickness(output, 2));
        }
    }

    private Rectangle? FindLargestRecordingHoldCandidate(DetectionResult result, int frameWidth, int frameHeight)
    {
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
        Bitmap bitmap;
        if (_isPlaybackMode && !ShouldDrawPlaybackOverlay())
        {
            bitmap = BitmapConverter.ToBitmap(preview);
        }
        else
        {
            using var displayPreview = CreateOverlayCanvas(preview);
            bitmap = BitmapConverter.ToBitmap(displayPreview);
        }

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
        Image? old = picCameraPreview.Image;
        picCameraPreview.Image = bitmap;
        old?.Dispose();
        lblFps.Text = Localization.T("Status.Fps", _fpsCounter.CurrentFps);
        if (_isPlaybackMode)
        {
            lblCameraStatus.Text = _playbackPlaying ? Localization.T("Status.PlaybackPlaying") : Localization.T("Status.PlaybackPaused");
            lblRtspStatus.Text = Localization.T("Status.File", Path.GetFileName(_playbackPath));
            lblLastFrame.Text = Localization.T("Status.Frame", _playbackFrameCount > 0 ? $"{_playbackFrameIndex + 1}/{_playbackFrameCount}" : $"{_playbackFrameIndex + 1}");
        }
        else
        {
            lblCameraStatus.Text = GetCameraStatusText();
            lblRtspStatus.Text = _settings.Camera.IsIpCamera ? $"RTSP: {_cameraService.ConnectionState}" : Localization.T("Status.RtspNone");
            lblLastFrame.Text = _lastFrameAt == DateTime.MinValue ? Localization.T("Status.LastFrameNone") : Localization.T("Status.LastFrameAgo", (DateTime.Now - _lastFrameAt).TotalSeconds);
        }

        lblDetectionStatus.Text = Localization.T("Status.DetectionMetrics", result.PersonDetected, result.RodMotionScore, result.HomeStable);
        lblAlgorithmStatus.Text = _lastAlgorithmEnabled ? Localization.T("Status.AlgorithmMetrics", _lastAlgorithmMs, _algorithmFpsCounter.CurrentFps) : Localization.T("Status.AlgorithmOff");
        lblRecordingStatus.Text = _isPlaybackMode ? Localization.T("Status.RecordingPlayback") : _recordingService.IsRecording ? Localization.T("Status.RecordingOn") : Localization.T("Status.RecordingState", FormatState(_stateMachine.CurrentState));
        UpdateControlStates();
        if (DateTime.Now - _lastDiskStatusAt > TimeSpan.FromSeconds(5))
        {
            _cachedDiskUsedPercent = DiskUtils.GetUsedPercent(_paths.RecVideos);
            _cachedDiskFreeBytes = DiskUtils.GetFreeDiskBytes(_paths.RecVideos);
            string root = Path.GetPathRoot(Path.GetFullPath(_paths.RecVideos)) ?? _paths.RecVideos;
            _cachedDiskTotalBytes = new DriveInfo(root).TotalSize;
            _cachedDiskStatus = Localization.T("Status.DiskUsage", _cachedDiskUsedPercent, DiskUtils.FormatBytes(_cachedDiskFreeBytes));
            _lastDiskStatusAt = DateTime.Now;
        }

        lblDiskStatus.Text = _cachedDiskStatus;
        lblErrorStatus.Text = Localization.T("Status.ErrorNone");
        UpdateDashboardState();
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
        double usedPercent = DiskUtils.GetUsedPercent(_paths.RecVideos);
        int stopThreshold = Math.Clamp(_settings.Storage.DiskStopThresholdPercent, 1, 100);
        int resumeThreshold = Math.Clamp(_settings.Storage.DiskResumeThresholdPercent, 1, stopThreshold);
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
        lblRecordingStatus.Text = Localization.T("Status.RecordingDisk", usedPercent);
        ShowRecordingStamp(Localization.T("Stamp.DiskFull"), Color.FromArgb(178, 34, 34));
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
            DateTime now = DateTime.Now;
            int cleanupHour = Math.Clamp(_settings.Storage.CleanupHour, 0, 23);
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
            string[] parts = resolution.Split('x');
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
            : !_recordingOnlyMode && rdoAutoRecording.Checked
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

    private void OpenSettingsDialog(string initialPage = "camera")
    {
        _settingsDialogOpen = true;
        try
        {
            using var form = new SettingsForm(
                _settings,
                ApplySettingsFromDialog,
                rdoFullRecording.Checked,
                _recordingOnlyMode,
                initialPage,
                () => new EventListForm(_eventLogService));
            form.ShowDialog(this);
        }
        finally
        {
            _settingsDialogOpen = false;
        }
    }

    private void ApplySettingsFromDialog()
    {
        Localization.SetLanguage(_settings.Language);
        ApplySettingsToUi();
        ApplyLocalization();
        _settingsManager.Save(_settings);
        StartCleanupSchedule(runStartupCleanup: false);
        lblCameraStatus.Text = Localization.T("Status.SettingsSaved");
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
            lblRecordingStatus.Text = Localization.T("Status.RecordingAutoWait");
            rdoFullRecording.Checked = true;
            await Task.Delay(TimeSpan.FromSeconds(2));
            ReadCameraSettingsFromUi();
            if (!_settings.Camera.IsIpCamera && !await EnsureUsbCameraSelectedAsync())
            {
                lblRecordingStatus.Text = Localization.T("Status.RecordingAutoNoUsb");
                _logger.Info("Full auto start skipped: no USB camera found.");
                return;
            }

            await OpenCameraPreviewAsync();
            if (!await WaitForFirstCameraFrameAsync(TimeSpan.FromSeconds(20)))
            {
                lblRecordingStatus.Text = Localization.T("Status.RecordingAutoNoFrame");
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
            lblRecordingStatus.Text = Localization.T("Status.RecordingAutoFailed", ex.Message);
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
        DateTime until = DateTime.Now + timeout;
        while (DateTime.Now < until)
        {
            if (IsDisposed)
            {
                return false;
            }

            if (HasRecentLatestFrame())
            {
                return true;
            }

            await Task.Delay(200);
        }

        return false;
    }

    private void UpdateCameraTypeUi()
    {
        bool usb = rdoUsbCamera.Checked;
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
            ? Localization.T("Status.CameraIp", _settings.Camera.IpCamera.IpAddress)
            : Localization.T("Status.CameraUsb", _settings.Camera.UsbCamera.DeviceIndex);
    }

    private string GetCameraErrorText()
    {
        return _settings.Camera.IsIpCamera
            ? Localization.T("Status.CameraIpError")
            : Localization.T("Status.CameraUsbError");
    }

    private static string FormatState(BlackboxState state)
    {
        return state switch
        {
            BlackboxState.Idle => Localization.T("State.Idle"),
            BlackboxState.Watching => Localization.T("State.Watching"),
            BlackboxState.PreEvent => Localization.T("State.PreEvent"),
            BlackboxState.Recording => Localization.T("State.Recording"),
            BlackboxState.WaitForStableHome => Localization.T("State.WaitForStableHome"),
            BlackboxState.Cooldown => Localization.T("State.Cooldown"),
            BlackboxState.Error => Localization.T("State.Error"),
            _ => state.ToString()
        };
    }

    private void InitializeFullScreenHint()
    {
        _fullScreenHint = new FullScreenHintControl
        {
            Text = Localization.T("FullScreen.Hint"),
            Visible = false
        };
        picCameraPreview.Controls.Add(_fullScreenHint);
        PositionFullScreenHint();

        _fullScreenHintTimer = new System.Windows.Forms.Timer { Interval = 40 };
        _fullScreenHintTimer.Tick += (_, _) => UpdateFullScreenHintFade();
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
        int width = Math.Max(1, Math.Max(_settings.Camera.ActiveWidth, rois.Max(roi => roi.X + roi.Width)));
        int height = Math.Max(1, Math.Max(_settings.Camera.ActiveHeight, rois.Max(roi => roi.Y + roi.Height)));
        return new System.Drawing.Size(width, height);
    }

    private RoiEditMode HitTestRoi(System.Drawing.Point point, Rectangle rect)
    {
        int tolerance = Math.Max(8, Math.Min(rect.Width, rect.Height) / 20);
        bool nearLeft = Math.Abs(point.X - rect.Left) <= tolerance;
        bool nearRight = Math.Abs(point.X - rect.Right) <= tolerance;
        bool nearTop = Math.Abs(point.Y - rect.Top) <= tolerance;
        bool nearBottom = Math.Abs(point.Y - rect.Bottom) <= tolerance;

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
        int dx = currentPoint.X - startPoint.X;
        int dy = currentPoint.Y - startPoint.Y;
        int left = startRect.Left;
        int right = startRect.Right;
        int top = startRect.Top;
        int bottom = startRect.Bottom;

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

        int x = Math.Clamp(rect.X, 0, Math.Max(0, image.Width - 1));
        int y = Math.Clamp(rect.Y, 0, Math.Max(0, image.Height - 1));
        int right = Math.Clamp(rect.Right, x + 1, image.Width);
        int bottom = Math.Clamp(rect.Bottom, y + 1, image.Height);
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

        double imageAspect = image.Width / (double)image.Height;
        double boxAspect = picCameraPreview.Width / (double)picCameraPreview.Height;
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

        int x = (int)((previewPoint.X - offsetX) * image.Width / (double)displayWidth);
        int y = (int)((previewPoint.Y - offsetY) * image.Height / (double)displayHeight);
        framePoint = new System.Drawing.Point(Math.Clamp(x, 0, image.Width - 1), Math.Clamp(y, 0, image.Height - 1));
        return true;
    }

    private async Task StartSelectedRecordingAsync()
    {
        if (!IsCameraPreviewOpen())
        {
            lblRecordingStatus.Text = Localization.T("Status.RecordingOpenCameraFirst");
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
            using var latestFrame = TryCloneLatestFrame();
            RotateFullRecordingIfDue(DateTime.Now, latestFrame);
        }

        lblRecordingStatus.Text = Localization.T("Status.RecordingFullInterval", GetFullInterval().TotalMinutes);
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
        lblRecordingStatus.Text = Localization.T("Status.RecordingStopped");
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
            string filePath = _recordingService.StopRecording(now);
            ShowRecordingStamp(Localization.T("Stamp.RecordingStopped"), Color.FromArgb(192, 92, 24));
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
        ShowRecordingStamp(Localization.T("Stamp.RecordingStarted"), Color.FromArgb(24, 132, 74));
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
            DateTime now = DateTime.Now;
            if (!CanStartRecordingOnDisk(now, "Manual"))
            {
                _stateMachine.RequestManualRecordingStop();
                _manualRecordingRequested = false;
                BeginInvoke(UpdateControlStates);
                return Task.CompletedTask;
            }

            using var latestFrame = TryCloneLatestFrame();
            _recordingService.StartRecording(now, "Manual", latestFrame);
            ShowRecordingStamp(Localization.T("Stamp.RecordingStarted"), Color.FromArgb(24, 132, 74));
            _currentRecordingStartedAt = now;
            _currentTriggerReason = "Manual";
            _maxMotionScore = 0;
            _maxRodMotionScore = 0;
            _minHomeDiffScore = double.MaxValue;
        }

        lblRecordingStatus.Text = Localization.T("Status.RecordingManualRequested");
        UpdateControlStates();
        return Task.CompletedTask;
    }

    private void StopManualRecording()
    {
        _stateMachine.RequestManualRecordingStop();
        _manualRecordingRequested = false;
        StopRecordingIfActive(DateTime.Now);
        lblRecordingStatus.Text = Localization.T("Status.RecordingStopped");
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

        string? filePath = _recordingService.StopRecording(endTime);
        ShowRecordingStamp(Localization.T("Stamp.RecordingStopped"), Color.FromArgb(192, 92, 24));
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
        double scale = OverlayScale(bitmap.Width, bitmap.Height);
        using var font = new Font("Malgun Gothic", OverlayFontSize(18F, scale), FontStyle.Bold);
        int padding = ScaleOverlayLength(12, scale);
        int margin = ScaleOverlayLength(18, scale);
        SizeF textSize = graphics.MeasureString(_recordingStampText, font);
        RectangleF rect = new RectangleF(margin, margin, textSize.Width + padding * 2, textSize.Height + padding);
        using var background = new SolidBrush(Color.FromArgb(220, _recordingStampBackColor));
        using var foreground = new SolidBrush(Color.White);
        graphics.FillRectangle(background, rect);
        using var border = new Pen(Color.White, OverlayPenWidth(scale));
        graphics.DrawRectangle(border, Rectangle.Round(rect));
        graphics.DrawString(_recordingStampText, font, foreground, rect.X + padding, rect.Y + padding / 2f);
    }

    private void DrawActiveRecordingStamp(Bitmap bitmap)
    {
        if (_recordingService?.IsRecording != true)
        {
            return;
        }

        bool blinkOn = (DateTime.Now.Millisecond / 350) % 2 == 0;
        if (!blinkOn)
        {
            return;
        }

        const string text = "REC";
        using var graphics = Graphics.FromImage(bitmap);
        double scale = OverlayScale(bitmap.Width, bitmap.Height);
        using var font = new Font("Malgun Gothic", OverlayFontSize(20F, scale), FontStyle.Bold);
        int padding = ScaleOverlayLength(12, scale);
        int margin = ScaleOverlayLength(18, scale);
        SizeF textSize = graphics.MeasureString(text, font);
        RectangleF rect = new RectangleF(bitmap.Width - textSize.Width - padding * 2 - margin, margin, textSize.Width + padding * 2, textSize.Height + padding);
        using var background = new SolidBrush(Color.FromArgb(230, 190, 24, 32));
        using var foreground = new SolidBrush(Color.White);
        graphics.FillRectangle(background, rect);
        using var border = new Pen(Color.White, OverlayPenWidth(scale));
        graphics.DrawRectangle(border, Rectangle.Round(rect));
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

        bool previewOpen = IsCameraPreviewOpen();
        bool recording = _recordingService.IsRecording;
        bool usb = rdoUsbCamera.Checked;
        bool cameraActionAvailable = !recording && !_cameraListRefreshInProgress;

        btnRefreshCamera.Enabled = usb && !_cameraListRefreshInProgress;
        btnCameraProperty.Enabled = usb && cmbCameraList.SelectedItem is int;
        btnConnectCamera.Enabled = cameraActionAvailable && !_cameraService.IsOpened;
        btnDisconnectCamera.Enabled = !_isPlaybackMode && (_cameraService.IsOpened || previewOpen);
        btnOpenCamera.Enabled = cameraActionAvailable && !previewOpen;
        btnCloseCamera.Enabled = !_isPlaybackMode && previewOpen;
        btnSaveHomeReference.Enabled = !_isPlaybackMode
            && previewOpen
            && HasRecentLatestFrame()
            && _lastFrameAt != DateTime.MinValue
            && DateTime.Now - _lastFrameAt < TimeSpan.FromSeconds(3);

        if (_isPlaybackMode)
        {
            btnSaveHomeReference.Enabled = false;
            btnWatchToggle.Enabled = false;
            btnStartRecording.Enabled = false;
            btnStopRecording.Enabled = false;
            playbackControl.Enabled = true;
            UpdateDashboardState();
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

        UpdateDashboardState();
    }

    private void UpdateDashboardState(bool force = false)
    {
        if (_recordingService is null || _paths is null || _settings is null)
        {
            return;
        }

        DateTime now = DateTime.Now;
        if (!force && now - _lastDashboardUpdateAt < TimeSpan.FromMilliseconds(250))
        {
            return;
        }

        _lastDashboardUpdateAt = now;
        bool previewOpen = IsCameraPreviewOpen();
        bool cameraConnected = _cameraService.IsOpened;
        bool recording = _recordingService.IsRecording;
        string cameraName = _settings.Camera.IsIpCamera
            ? Localization.T("Dashboard.IpCamera")
            : Localization.T("Dashboard.UsbCamera", _settings.Camera.UsbCamera.DeviceIndex);
        string cameraAddress = _settings.Camera.IsIpCamera
            ? _settings.Camera.IpCamera.IpAddress
            : Localization.T("Dashboard.UsbDevice", _settings.Camera.UsbCamera.DeviceIndex);
        string connectionText = cameraConnected
            ? Localization.T("Dashboard.Connected")
            : previewOpen
                ? Localization.T("Dashboard.Connecting")
                : Localization.T("Dashboard.Disconnected");
        string connectionDot = cameraConnected ? "●" : "●";

        lblHeaderCamera.Text = $"{connectionDot} {Localization.T("Dashboard.Camera")}: {connectionText}";
        lblHeaderCamera.ForeColor = cameraConnected ? UiTheme.Success : UiTheme.Warning;
        lblCameraCardState.Text = $"{Localization.T("Dashboard.ConnectionState")}    {connectionText}";
        lblCameraCardState.ForeColor = cameraConnected ? UiTheme.Success : UiTheme.MutedText;
        lblCameraCardAddress.Text = $"{Localization.T("Dashboard.CameraAddress")}  {cameraAddress}";
        lblCameraCardStream.Text = previewOpen
            ? $"{Localization.T("Dashboard.StreamState")}  {Localization.T("Dashboard.StreamActive", _fpsCounter.CurrentFps, _settings.Camera.ActiveWidth, _settings.Camera.ActiveHeight)}"
            : $"{Localization.T("Dashboard.StreamState")}  {Localization.T("Dashboard.StreamStopped")}";

        TimeSpan elapsed = recording && _currentRecordingStartedAt != default
            ? now - _currentRecordingStartedAt
            : TimeSpan.Zero;
        string elapsedText = FormatElapsed(elapsed);
        string recordingState = recording ? Localization.T("Dashboard.Recording") : Localization.T("Dashboard.RecordingOff");
        lblHeaderRecording.Text = recording ? $"● REC {elapsedText}" : $"● {recordingState}";
        lblHeaderRecording.ForeColor = recording ? UiTheme.Danger : Color.White;
        lblRecordingCardState.Text = $"{Localization.T("Dashboard.State")}       {recordingState}";
        lblRecordingCardState.ForeColor = recording ? UiTheme.Danger : UiTheme.MutedText;
        string activePath = recording ? _recordingService.ActiveRecordingPath : "";
        string activeFile = string.IsNullOrWhiteSpace(activePath) ? "-" : Path.GetFileName(activePath).Replace(".recording.mp4", ".mp4", StringComparison.OrdinalIgnoreCase);
        if (recording && File.Exists(activePath))
        {
            try
            {
                activeFile += $" ({DiskUtils.FormatBytes(new FileInfo(activePath).Length)})";
            }
            catch
            {
            }
        }

        lblRecordingCardFile.Text = $"{Localization.T("Dashboard.CurrentFile")}  {activeFile}";
        lblRecordingCardElapsed.Text = $"{Localization.T("Dashboard.Elapsed")}  {elapsedText}";
        lblRecordingCardLocation.Text = $"{Localization.T("Dashboard.SaveLocation")}  {CompactPath(_paths.RecVideos, 42)}";

        if (_cachedDiskTotalBytes <= 0)
        {
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(_paths.RecVideos)) ?? _paths.RecVideos;
                var drive = new DriveInfo(root);
                _cachedDiskTotalBytes = drive.TotalSize;
                _cachedDiskFreeBytes = drive.AvailableFreeSpace;
                _cachedDiskUsedPercent = drive.TotalSize <= 0 ? 0 : (drive.TotalSize - drive.AvailableFreeSpace) * 100.0 / drive.TotalSize;
            }
            catch
            {
            }
        }

        string driveRoot = Path.GetPathRoot(Path.GetFullPath(_paths.RecVideos)) ?? _paths.RecVideos;
        lblHeaderStorage.Text = $"{CompactPath(_paths.RecVideos, 26)} ({DiskUtils.FormatBytes(_cachedDiskFreeBytes)} {Localization.T("Dashboard.Free")})";
        lblStorageCardDrive.Text = $"{Localization.T("Dashboard.Drive")}  {driveRoot} ({DiskUtils.FormatBytes(_cachedDiskFreeBytes)} {Localization.T("Dashboard.Free")})";
        long usedBytes = Math.Max(0, _cachedDiskTotalBytes - _cachedDiskFreeBytes);
        lblStorageCardUsage.Text = $"{Localization.T("Dashboard.Usage")}    {DiskUtils.FormatBytes(usedBytes)} / {DiskUtils.FormatBytes(_cachedDiskTotalBytes)} ({_cachedDiskUsedPercent:0}%)";
        storageUsageBar.Value = Math.Clamp((int)Math.Round(_cachedDiskUsedPercent), storageUsageBar.Minimum, storageUsageBar.Maximum);

        lblVideoCamera.Text = cameraName;
        lblVideoInfo.Text = $"FPS: {_fpsCounter.CurrentFps:0}";
        lblVideoRecording.Text = $"● REC {elapsedText}";
        lblVideoRecording.Visible = recording && !_fullScreenMode;
        lblVersion.Text = $"v{GetDisplayVersion()}";
        PositionVideoOverlays();
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        int hours = Math.Max(0, (int)elapsed.TotalHours);
        return $"{hours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }

    private static string CompactPath(string path, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length <= maxLength)
        {
            return path;
        }

        string root = Path.GetPathRoot(path) ?? "";
        string leaf = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return $"{root}…{Path.DirectorySeparatorChar}{leaf}";
    }

    private static string GetDisplayVersion()
    {
        string version = Application.ProductVersion;
        int separator = version.IndexOf('+');
        return separator >= 0 ? version[..separator] : version;
    }

    private void PositionVideoOverlays()
    {
        if (videoPanel.IsDisposed || picCameraPreview.IsDisposed)
        {
            return;
        }

        const int margin = 14;
        Rectangle previewBounds = picCameraPreview.Bounds;
        lblVideoRecording.Location = new System.Drawing.Point(
            Math.Max(margin, previewBounds.Right - lblVideoRecording.Width - margin - videoPanel.Padding.Right),
            previewBounds.Top + margin);
        lblVideoCamera.Location = new System.Drawing.Point(previewBounds.Left + margin, Math.Max(margin, previewBounds.Bottom - lblVideoCamera.Height - margin));
        lblVideoInfo.Location = new System.Drawing.Point(
            Math.Max(margin, previewBounds.Right - lblVideoInfo.Width - margin - videoPanel.Padding.Right),
            Math.Max(margin, previewBounds.Bottom - lblVideoInfo.Height - margin));
        lblVideoCamera.BringToFront();
        lblVideoInfo.BringToFront();
        lblVideoRecording.BringToFront();
    }

    private void SetLatestFrame(Mat frame)
    {
        lock (_latestFrameSync)
        {
            Mat? old = _latestFrame;
            _latestFrame = frame.Clone();
            old?.Dispose();
        }
    }

    private Mat? TryCloneLatestFrame()
    {
        lock (_latestFrameSync)
        {
            try
            {
                return _latestFrame is not null && !_latestFrame.Empty()
                    ? _latestFrame.Clone()
                    : null;
            }
            catch (ObjectDisposedException)
            {
                _latestFrame = null;
                return null;
            }
        }
    }

    private bool HasRecentLatestFrame()
    {
        if (_lastFrameAt == DateTime.MinValue || DateTime.Now - _lastFrameAt >= TimeSpan.FromSeconds(3))
        {
            return false;
        }

        using var frame = TryCloneLatestFrame();
        return frame is not null;
    }

    private void ClearLatestFrame()
    {
        lock (_latestFrameSync)
        {
            _latestFrame?.Dispose();
            _latestFrame = null;
        }
    }

    private void SaveHomeReference()
    {
        using var frame = TryCloneLatestFrame();
        if (frame is null)
        {
            return;
        }

        try
        {
            _detectionService.SaveHomeReference(frame, _settings.Rois.RodHomeRoi, _paths.HomeReferencePath);
            _logger.Info("Home reference saved.");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Saving home reference failed");
            lblErrorStatus.Text = Localization.T("Status.Error", ex.Message);
        }
    }

    private void SaveDetectionBaselineReference()
    {
        using var frame = TryCloneLatestFrame();
        if (frame is null)
        {
            lblDetectionStatus.Text = Localization.T("Status.DetectionNoFrame");
            return;
        }

        try
        {
            _detectionService.SaveBaselineReference(frame, _paths.BaselineReferencePath);
            _logger.Info($"Detection baseline reference saved: {_paths.BaselineReferencePath}");
            lblDetectionStatus.Text = Localization.T("Status.DetectionBaselineSaved");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Saving detection baseline failed");
            lblErrorStatus.Text = Localization.T("Status.Error", ex.Message);
        }
    }

    private void OpenCameraPropertyDialog()
    {
        if (_settings.Camera.IsIpCamera)
        {
            MessageBox.Show(this, Localization.T("Msg.UsbPropertyOnly"), "DFBlackbox", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var form = new CameraSettingsForm(_cameraService, _settings.Camera.UsbCamera);
        form.ShowDialog(this);
        _settingsManager.Save(_settings);
    }

    private void MainForm_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F11)
        {
            if (!_settingsDialogOpen)
            {
                ToggleFullScreen();
            }

            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

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

    private void ToggleFullScreen()
    {
        if (_fullScreenMode)
        {
            ExitFullScreen();
        }
        else
        {
            EnterFullScreen();
        }
    }

    private void EnterFullScreen()
    {
        if (_fullScreenMode)
        {
            return;
        }

        _normalWindowState = WindowState;
        _normalBorderStyle = FormBorderStyle;
        _normalTopMost = TopMost;
        _normalBounds = Bounds;
        _normalSidePanelVisible = sidePanel.Visible;
        _normalStatusStripVisible = statusStrip.Visible;
        _normalTopHeaderVisible = topHeaderPanel.Visible;
        _normalPlaybackPanelVisible = playbackPanel.Visible;
        _normalVideoCameraVisible = lblVideoCamera.Visible;
        _normalVideoInfoVisible = lblVideoInfo.Visible;
        _normalVideoRecordingVisible = lblVideoRecording.Visible;
        _normalMainContentPadding = mainContentPanel.Padding;
        _normalVideoPanelPadding = videoPanel.Padding;

        _fullScreenMode = true;
        sidePanel.Visible = false;
        statusStrip.Visible = false;
        topHeaderPanel.Visible = false;
        playbackPanel.Visible = false;
        lblVideoCamera.Visible = false;
        lblVideoInfo.Visible = false;
        lblVideoRecording.Visible = false;
        mainContentPanel.Padding = Padding.Empty;
        videoPanel.Padding = Padding.Empty;

        SuspendLayout();
        WindowState = FormWindowState.Normal;
        FormBorderStyle = FormBorderStyle.None;
        Bounds = Screen.FromControl(this).Bounds;
        TopMost = true;
        ResumeLayout(true);

        ShowFullScreenHint();
    }

    private void ExitFullScreen()
    {
        if (!_fullScreenMode)
        {
            return;
        }

        _fullScreenMode = false;
        _fullScreenHintTimer?.Stop();
        if (_fullScreenHint is not null)
        {
            _fullScreenHint.Visible = false;
        }

        SuspendLayout();
        TopMost = _normalTopMost;
        FormBorderStyle = _normalBorderStyle;
        Bounds = _normalBounds;
        WindowState = _normalWindowState;
        sidePanel.Visible = _normalSidePanelVisible;
        statusStrip.Visible = _normalStatusStripVisible;
        topHeaderPanel.Visible = _normalTopHeaderVisible;
        playbackPanel.Visible = _normalPlaybackPanelVisible;
        lblVideoCamera.Visible = _normalVideoCameraVisible;
        lblVideoInfo.Visible = _normalVideoInfoVisible;
        lblVideoRecording.Visible = _normalVideoRecordingVisible;
        mainContentPanel.Padding = _normalMainContentPadding;
        videoPanel.Padding = _normalVideoPanelPadding;
        ResumeLayout(true);
        PositionPlaybackPanel();
    }

    private void ShowFullScreenHint()
    {
        if (_fullScreenHint is null || _fullScreenHintTimer is null)
        {
            return;
        }

        PositionFullScreenHint();
        _fullScreenHint.Opacity = 255;
        _fullScreenHint.Visible = true;
        _fullScreenHint.BringToFront();
        _fullScreenHintStartedAt = DateTime.Now;
        _fullScreenHintTimer.Stop();
        _fullScreenHintTimer.Start();
    }

    private void UpdateFullScreenHintFade()
    {
        if (_fullScreenHint is null || _fullScreenHintTimer is null)
        {
            return;
        }

        double elapsed = (DateTime.Now - _fullScreenHintStartedAt).TotalMilliseconds;
        const double holdMs = 900;
        const double fadeMs = 1800;
        if (elapsed <= holdMs)
        {
            _fullScreenHint.Opacity = 255;
            return;
        }

        double fadeRatio = Math.Clamp((elapsed - holdMs) / fadeMs, 0, 1);
        _fullScreenHint.Opacity = (int)Math.Round(255 * (1 - fadeRatio));
        if (fadeRatio >= 1)
        {
            _fullScreenHintTimer.Stop();
            _fullScreenHint.Visible = false;
        }
    }

    private void PositionFullScreenHint()
    {
        if (_fullScreenHint is null)
        {
            return;
        }

        _fullScreenHint.Bounds = new Rectangle(0, 28, picCameraPreview.ClientSize.Width, 60);
        _fullScreenHint.BringToFront();
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
        await ExitPlaybackModeAsync(clearPreview: false);
        _cleanupTimer?.Dispose();
        _autoStartFullRecordingTimer?.Dispose();
        _fullScreenHintTimer?.Dispose();
        _recordingService?.Dispose();
        _detectionService.Dispose();
        _cameraService.Dispose();
        ClearLatestFrame();
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
        PositionFullScreenHint();
        PositionPlaybackPanel();
        PositionVideoOverlays();
        if (WindowState == FormWindowState.Minimized)
        {
            HideToTray();
        }
    }

    private void PositionPlaybackPanel()
    {
        if (playbackPanel.IsDisposed || playbackControl.IsDisposed || sidePanel.IsDisposed)
        {
            return;
        }

        int availableWidth = playbackPanel.ClientSize.Width - playbackPanel.Padding.Horizontal;
        if (availableWidth <= 0)
        {
            return;
        }

        playbackControl.Width = Math.Max(1, availableWidth);
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
            Cv2.Rectangle(output, ToCvRect(ScaleRoiForFrame(roi, output).ToRectangle()), Scalar.Black, -1);
        }
    }

    private static void DrawRoi(Mat output, RoiRect roi, Scalar color, string label, int thickness)
    {
        var rect = ToCvRect(roi.ToRectangle());
        int lineThickness = OverlayThickness(output, thickness);
        Cv2.Rectangle(output, rect, color, lineThickness);
        Cv2.PutText(
            output,
            label,
            new OpenCvSharp.Point(rect.X + OverlayLength(output, 4), Math.Max(OverlayLength(output, 18), rect.Y - OverlayLength(output, 6))),
            HersheyFonts.HersheySimplex,
            OverlayFontScale(output, 0.55),
            color,
            lineThickness);
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
        int half = OverlayLength(output, 5);
        int left = Math.Clamp(x - half, 0, Math.Max(0, output.Width - 1));
        int top = Math.Clamp(y - half, 0, Math.Max(0, output.Height - 1));
        int right = Math.Clamp(x + half, 0, Math.Max(0, output.Width - 1));
        int bottom = Math.Clamp(y + half, 0, Math.Max(0, output.Height - 1));
        Cv2.Rectangle(output, new Rect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top)), color, -1);
    }

    private static OpenCvSharp.Rect ToCvRect(Rectangle rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    private static Mat CreateOverlayCanvas(Mat frame)
    {
        int targetHeight = (int)OverlayReferenceHeight;
        int targetWidth = Math.Max(2, (int)Math.Round(frame.Width * (targetHeight / (double)Math.Max(1, frame.Height))));
        if (targetWidth % 2 != 0)
        {
            targetWidth++;
        }

        if (frame.Width == targetWidth && frame.Height == targetHeight)
        {
            return frame.Clone();
        }

        var output = new Mat();
        Cv2.Resize(frame, output, new OpenCvSharp.Size(targetWidth, targetHeight), 0, 0, InterpolationFlags.Linear);
        return output;
    }

    private static Rectangle ScaleRectToOverlay(Rectangle rect, int sourceWidth, int sourceHeight, Mat overlay)
    {
        double scaleX = overlay.Width / (double)Math.Max(1, sourceWidth);
        double scaleY = overlay.Height / (double)Math.Max(1, sourceHeight);
        int x = (int)Math.Round(rect.X * scaleX);
        int y = (int)Math.Round(rect.Y * scaleY);
        int width = Math.Max(1, (int)Math.Round(rect.Width * scaleX));
        int height = Math.Max(1, (int)Math.Round(rect.Height * scaleY));
        return new Rectangle(x, y, width, height);
    }

    private static int OverlayLength(Mat frame, int value)
    {
        return value;
    }

    private static int OverlayThickness(Mat frame, int value)
    {
        return Math.Max(1, value);
    }

    private static double OverlayFontScale(Mat frame, double value)
    {
        return value;
    }

    private static OpenCvSharp.Point OverlayPoint(Mat frame, int x, int y)
    {
        double scale = OverlayScale(frame);
        return new OpenCvSharp.Point(ScaleOverlayLength(x, scale), ScaleOverlayLength(y, scale));
    }

    private static OpenCvSharp.Point OverlayBottomLeftPoint(Mat frame, int x, int bottomMargin)
    {
        double scale = OverlayScale(frame);
        return new OpenCvSharp.Point(ScaleOverlayLength(x, scale), frame.Height - ScaleOverlayLength(bottomMargin, scale));
    }

    private static double OverlayScale(Mat frame)
    {
        return 1.0;
    }

    private static double OverlayScale(int width, int height)
    {
        return 1.0;
    }

    private static int ScaleOverlayLength(int value, double scale)
    {
        return Math.Max(1, value);
    }

    private static float OverlayFontSize(float points, double scale)
    {
        return Math.Max(1F, points);
    }

    private static float OverlayPenWidth(double scale)
    {
        return 2F;
    }

    private sealed class FullScreenHintControl : Control
    {
        private int _opacity = 255;

        public int Opacity
        {
            get => _opacity;
            set
            {
                int clamped = Math.Clamp(value, 0, 255);
                if (_opacity == clamped)
                {
                    return;
                }

                _opacity = clamped;
                Invalidate();
            }
        }

        public FullScreenHintControl()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.UserPaint
                | ControlStyles.SupportsTransparentBackColor,
                true);
            BackColor = Color.Transparent;
            Font = new Font("Malgun Gothic", 20F, FontStyle.Bold);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Opacity <= 0 || string.IsNullOrWhiteSpace(Text))
            {
                return;
            }

            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            int alpha = Math.Clamp(Opacity, 0, 255);
            using var shadow = new SolidBrush(Color.FromArgb(alpha * 160 / 255, Color.Black));
            using var foreground = new SolidBrush(Color.FromArgb(alpha, Color.White));
            SizeF size = e.Graphics.MeasureString(Text, Font);
            float x = (Width - size.Width) / 2F;
            float y = (Height - size.Height) / 2F;
            e.Graphics.DrawString(Text, Font, shadow, x + 2, y + 2);
            e.Graphics.DrawString(Text, Font, foreground, x, y);
        }
    }

}
