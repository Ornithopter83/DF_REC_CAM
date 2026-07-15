namespace DFBlackbox.Forms;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null;
    private MenuStrip menuStrip;
    private ToolStripMenuItem mnuLanguage;
    private ToolStripMenuItem mnuLanguageKor;
    private ToolStripMenuItem mnuLanguageEng;
    private Panel topHeaderPanel;
    private Label lblHeaderAppName;
    private Label lblHeaderCamera;
    private Label lblHeaderRecording;
    private Label lblHeaderStorage;
    private Panel mainContentPanel;
    private Panel videoPanel;
    private PictureBox picCameraPreview;
    private Label lblVideoCamera;
    private Label lblVideoInfo;
    private Label lblVideoRecording;
    private Panel playbackPanel;
    private PlaybackControl playbackControl;
    private Krypton.Toolkit.KryptonPanel sidePanel;
    private TableLayoutPanel sideTabHeaderPanel;
    private Krypton.Toolkit.KryptonButton btnRecordingTab;
    private Krypton.Toolkit.KryptonButton btnPlaybackTab;
    private ToolTip mainToolTip;
    private FlowLayoutPanel sideContentPanel;
    private Panel playbackTabPanel;
    private StatusStrip statusStrip;
    private Krypton.Toolkit.KryptonRadioButton rdoUsbCamera;
    private Krypton.Toolkit.KryptonRadioButton rdoIpCamera;
    private Krypton.Toolkit.KryptonComboBox cmbCameraList;
    private Krypton.Toolkit.KryptonComboBox cmbResolution;
    private Krypton.Toolkit.KryptonComboBox cmbFps;
    private Krypton.Toolkit.KryptonTextBox txtIpAddress;
    private Krypton.Toolkit.KryptonNumericUpDown numRtspPort;
    private Krypton.Toolkit.KryptonNumericUpDown numHttpPort;
    private Krypton.Toolkit.KryptonComboBox cmbStreamPath;
    private Krypton.Toolkit.KryptonCheckBox chkUseManualRtspUrl;
    private Krypton.Toolkit.KryptonTextBox txtManualRtspUrl;
    private Krypton.Toolkit.KryptonTextBox txtGeneratedRtspUrl;
    private Krypton.Toolkit.KryptonButton btnRefreshCamera;
    private Krypton.Toolkit.KryptonButton btnApplyCamera;
    private Krypton.Toolkit.KryptonButton btnWatchToggle;
    private Krypton.Toolkit.KryptonButton btnDefaultSettings;
    private Krypton.Toolkit.KryptonButton btnConnectCamera;
    private Krypton.Toolkit.KryptonButton btnDisconnectCamera;
    private Krypton.Toolkit.KryptonButton btnOpenCamera;
    private Krypton.Toolkit.KryptonButton btnCloseCamera;
    private Krypton.Toolkit.KryptonButton btnLoadVideoFile;
    private Krypton.Toolkit.KryptonButton btnCameraProperty;
    private Krypton.Toolkit.KryptonButton btnOpenStorageFolder;
    private Krypton.Toolkit.KryptonButton btnSettings;
    private Krypton.Toolkit.KryptonButton btnCameraSettings;
    private Krypton.Toolkit.KryptonButton btnRecordingSettings;
    private Krypton.Toolkit.KryptonButton btnStorageSettings;
    private Krypton.Toolkit.KryptonRadioButton rdoManualRecording;
    private Krypton.Toolkit.KryptonRadioButton rdoAutoRecording;
    private Krypton.Toolkit.KryptonRadioButton rdoFullRecording;
    private Krypton.Toolkit.KryptonButton btnStartRecording;
    private Krypton.Toolkit.KryptonButton btnStopRecording;
    private Krypton.Toolkit.KryptonButton btnSaveHomeReference;
    private Krypton.Toolkit.KryptonButton btnOpenEventList;
    private Krypton.Toolkit.KryptonCheckBox chkShowPersonBox;
    private Krypton.Toolkit.KryptonCheckBox chkShowMotionMask;
    private Krypton.Toolkit.KryptonCheckBox chkShowRodRoi;
    private Krypton.Toolkit.KryptonCheckBox chkShowHomeRoi;
    private Krypton.Toolkit.KryptonCheckBox chkShowDebugText;
    private Krypton.Toolkit.KryptonCheckBox chkShowRecordingStatus;
    private Krypton.Toolkit.KryptonCheckBox chkShowFrameTime;
    private Krypton.Toolkit.KryptonNumericUpDown numMotionThreshold;
    private Krypton.Toolkit.KryptonNumericUpDown numMinMotionArea;
    private Krypton.Toolkit.KryptonNumericUpDown numPersonThreshold;
    private Krypton.Toolkit.KryptonNumericUpDown numRodThreshold;
    private Krypton.Toolkit.KryptonNumericUpDown numStrongRodThreshold;
    private Krypton.Toolkit.KryptonNumericUpDown numHomeDiffThreshold;
    private Krypton.Toolkit.KryptonNumericUpDown numHomeMotionThreshold;
    private ToolStripStatusLabel lblCameraStatus;
    private ToolStripStatusLabel lblRtspStatus;
    private ToolStripStatusLabel lblLastFrame;
    private ToolStripStatusLabel lblFps;
    private ToolStripStatusLabel lblDetectionStatus;
    private ToolStripStatusLabel lblAlgorithmStatus;
    private ToolStripStatusLabel lblRecordingStatus;
    private ToolStripStatusLabel lblDiskStatus;
    private ToolStripStatusLabel lblErrorStatus;
    private ToolStripStatusLabel lblVersion;
    private Label lblCameraCardState;
    private Label lblCameraCardAddress;
    private Label lblCameraCardStream;
    private Label lblRecordingCardState;
    private Label lblRecordingCardFile;
    private Label lblRecordingCardElapsed;
    private Label lblRecordingCardLocation;
    private Label lblStorageCardDrive;
    private Label lblStorageCardUsage;
    private ProgressBar storageUsageBar;
    private Panel legacyControlHost;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _settingsGearImage?.Dispose();
            _settingsGearImage = null;
        }

        if (disposing && components != null)
        {
            components.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        menuStrip = new MenuStrip();
        mnuLanguage = new ToolStripMenuItem();
        mnuLanguageKor = new ToolStripMenuItem();
        mnuLanguageEng = new ToolStripMenuItem();
        topHeaderPanel = new Panel();
        lblHeaderAppName = new Label();
        lblHeaderCamera = new Label();
        lblHeaderRecording = new Label();
        lblHeaderStorage = new Label();
        mainContentPanel = new Panel();
        videoPanel = new Panel();
        picCameraPreview = new PictureBox();
        lblVideoCamera = new Label();
        lblVideoInfo = new Label();
        lblVideoRecording = new Label();
        playbackPanel = new Panel();
        playbackControl = new PlaybackControl();
        sidePanel = new Krypton.Toolkit.KryptonPanel();
        sideTabHeaderPanel = new TableLayoutPanel();
        btnRecordingTab = new Krypton.Toolkit.KryptonButton();
        btnPlaybackTab = new Krypton.Toolkit.KryptonButton();
        mainToolTip = new ToolTip(components);
        sideContentPanel = new FlowLayoutPanel();
        playbackTabPanel = new Panel();
        statusStrip = new StatusStrip();
        rdoUsbCamera = new Krypton.Toolkit.KryptonRadioButton();
        rdoIpCamera = new Krypton.Toolkit.KryptonRadioButton();
        cmbCameraList = new Krypton.Toolkit.KryptonComboBox();
        cmbResolution = new Krypton.Toolkit.KryptonComboBox();
        cmbFps = new Krypton.Toolkit.KryptonComboBox();
        txtIpAddress = new Krypton.Toolkit.KryptonTextBox();
        numRtspPort = new Krypton.Toolkit.KryptonNumericUpDown();
        numHttpPort = new Krypton.Toolkit.KryptonNumericUpDown();
        cmbStreamPath = new Krypton.Toolkit.KryptonComboBox();
        chkUseManualRtspUrl = new Krypton.Toolkit.KryptonCheckBox();
        txtManualRtspUrl = new Krypton.Toolkit.KryptonTextBox();
        txtGeneratedRtspUrl = new Krypton.Toolkit.KryptonTextBox();
        btnRefreshCamera = new Krypton.Toolkit.KryptonButton();
        btnApplyCamera = new Krypton.Toolkit.KryptonButton();
        btnWatchToggle = new Krypton.Toolkit.KryptonButton();
        btnDefaultSettings = new Krypton.Toolkit.KryptonButton();
        btnConnectCamera = new Krypton.Toolkit.KryptonButton();
        btnDisconnectCamera = new Krypton.Toolkit.KryptonButton();
        btnOpenCamera = new Krypton.Toolkit.KryptonButton();
        btnCloseCamera = new Krypton.Toolkit.KryptonButton();
        btnLoadVideoFile = new Krypton.Toolkit.KryptonButton();
        btnCameraProperty = new Krypton.Toolkit.KryptonButton();
        btnOpenStorageFolder = new Krypton.Toolkit.KryptonButton();
        btnSettings = new Krypton.Toolkit.KryptonButton();
        btnCameraSettings = new Krypton.Toolkit.KryptonButton();
        btnRecordingSettings = new Krypton.Toolkit.KryptonButton();
        btnStorageSettings = new Krypton.Toolkit.KryptonButton();
        rdoManualRecording = new Krypton.Toolkit.KryptonRadioButton();
        rdoAutoRecording = new Krypton.Toolkit.KryptonRadioButton();
        rdoFullRecording = new Krypton.Toolkit.KryptonRadioButton();
        btnStartRecording = new Krypton.Toolkit.KryptonButton();
        btnStopRecording = new Krypton.Toolkit.KryptonButton();
        btnSaveHomeReference = new Krypton.Toolkit.KryptonButton();
        btnOpenEventList = new Krypton.Toolkit.KryptonButton();
        chkShowPersonBox = new Krypton.Toolkit.KryptonCheckBox();
        chkShowMotionMask = new Krypton.Toolkit.KryptonCheckBox();
        chkShowRodRoi = new Krypton.Toolkit.KryptonCheckBox();
        chkShowHomeRoi = new Krypton.Toolkit.KryptonCheckBox();
        chkShowDebugText = new Krypton.Toolkit.KryptonCheckBox();
        chkShowRecordingStatus = new Krypton.Toolkit.KryptonCheckBox();
        chkShowFrameTime = new Krypton.Toolkit.KryptonCheckBox();
        numMotionThreshold = new Krypton.Toolkit.KryptonNumericUpDown();
        numMinMotionArea = new Krypton.Toolkit.KryptonNumericUpDown();
        numPersonThreshold = new Krypton.Toolkit.KryptonNumericUpDown();
        numRodThreshold = new Krypton.Toolkit.KryptonNumericUpDown();
        numStrongRodThreshold = new Krypton.Toolkit.KryptonNumericUpDown();
        numHomeDiffThreshold = new Krypton.Toolkit.KryptonNumericUpDown();
        numHomeMotionThreshold = new Krypton.Toolkit.KryptonNumericUpDown();
        lblCameraStatus = new ToolStripStatusLabel();
        lblRtspStatus = new ToolStripStatusLabel();
        lblLastFrame = new ToolStripStatusLabel();
        lblFps = new ToolStripStatusLabel();
        lblDetectionStatus = new ToolStripStatusLabel();
        lblAlgorithmStatus = new ToolStripStatusLabel();
        lblRecordingStatus = new ToolStripStatusLabel();
        lblDiskStatus = new ToolStripStatusLabel();
        lblErrorStatus = new ToolStripStatusLabel();
        lblVersion = new ToolStripStatusLabel();
        lblCameraCardState = new Label();
        lblCameraCardAddress = new Label();
        lblCameraCardStream = new Label();
        lblRecordingCardState = new Label();
        lblRecordingCardFile = new Label();
        lblRecordingCardElapsed = new Label();
        lblRecordingCardLocation = new Label();
        lblStorageCardDrive = new Label();
        lblStorageCardUsage = new Label();
        storageUsageBar = new ProgressBar();
        legacyControlHost = new Panel();
        ((System.ComponentModel.ISupportInitialize)picCameraPreview).BeginInit();
        SuspendLayout();

        menuStrip.Items.AddRange(new ToolStripItem[] { mnuLanguage });
        menuStrip.Name = "menuStrip";
        menuStrip.Dock = DockStyle.Top;
        menuStrip.Visible = false;
        mnuLanguage.DropDownItems.AddRange(new ToolStripItem[] { mnuLanguageKor, mnuLanguageEng });
        mnuLanguage.Name = "mnuLanguage";
        mnuLanguageKor.Name = "mnuLanguageKor";
        mnuLanguageEng.Name = "mnuLanguageEng";

        topHeaderPanel.BackColor = Color.FromArgb(8, 27, 51);
        topHeaderPanel.Dock = DockStyle.Top;
        topHeaderPanel.Height = 48;
        topHeaderPanel.Name = "topHeaderPanel";
        var headerLayout = new TableLayoutPanel
        {
            BackColor = Color.Transparent,
            ColumnCount = 4,
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 0, 16, 0),
            RowCount = 1
        };
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28F));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24F));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28F));
        ConfigureHeaderLabel(lblHeaderAppName, "DFBlackbox", ContentAlignment.MiddleLeft, true);
        ConfigureHeaderLabel(lblHeaderCamera, "● 카메라: 대기", ContentAlignment.MiddleCenter);
        ConfigureHeaderLabel(lblHeaderRecording, "● 녹화 꺼짐", ContentAlignment.MiddleCenter);
        ConfigureHeaderLabel(lblHeaderStorage, "저장소 확인 중", ContentAlignment.MiddleRight);
        headerLayout.Controls.Add(lblHeaderAppName, 0, 0);
        headerLayout.Controls.Add(lblHeaderCamera, 1, 0);
        headerLayout.Controls.Add(lblHeaderRecording, 2, 0);
        headerLayout.Controls.Add(lblHeaderStorage, 3, 0);
        topHeaderPanel.Controls.Add(headerLayout);

        mainContentPanel.BackColor = Color.FromArgb(245, 247, 250);
        mainContentPanel.Dock = DockStyle.Fill;
        mainContentPanel.Name = "mainContentPanel";
        mainContentPanel.Padding = new Padding(8);

        videoPanel.BackColor = Color.Black;
        videoPanel.Dock = DockStyle.Fill;
        videoPanel.Name = "videoPanel";
        videoPanel.Padding = new Padding(0, 0, 8, 0);

        picCameraPreview.BackColor = Color.Black;
        picCameraPreview.Dock = DockStyle.Fill;
        picCameraPreview.Name = "picCameraPreview";
        picCameraPreview.SizeMode = PictureBoxSizeMode.Zoom;

        ConfigureVideoOverlayLabel(lblVideoCamera, "IP 카메라");
        ConfigureVideoOverlayLabel(lblVideoInfo, "FPS: 0");
        ConfigureVideoOverlayLabel(lblVideoRecording, "● REC 00:00:00");
        lblVideoRecording.ForeColor = Color.FromArgb(255, 72, 72);
        lblVideoRecording.Visible = false;

        playbackPanel.BackColor = Color.Transparent;
        playbackPanel.Dock = DockStyle.Fill;
        playbackPanel.Margin = new Padding(0);
        playbackPanel.Name = "playbackPanel";
        playbackPanel.Padding = new Padding(0, 8, 0, 0);
        playbackPanel.Visible = false;

        playbackControl.Dock = DockStyle.Top;
        playbackControl.Margin = new Padding(0);
        playbackControl.Name = "playbackControl";
        playbackControl.Size = new Size(398, 228);

        sidePanel.Dock = DockStyle.Right;
        sidePanel.Name = "sidePanel";
        sidePanel.Width = 430;

        sideTabHeaderPanel.BackColor = Color.Transparent;
        sideTabHeaderPanel.ColumnCount = 3;
        sideTabHeaderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        sideTabHeaderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        sideTabHeaderPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44F));
        sideTabHeaderPanel.Dock = DockStyle.Top;
        sideTabHeaderPanel.Height = 48;
        sideTabHeaderPanel.Name = "sideTabHeaderPanel";
        sideTabHeaderPanel.Padding = new Padding(10, 4, 6, 4);
        sideTabHeaderPanel.RowCount = 1;
        sideTabHeaderPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        btnRecordingTab.Dock = DockStyle.Fill;
        btnRecordingTab.Margin = new Padding(0, 0, 4, 0);
        btnRecordingTab.Name = "btnRecordingTab";
        btnRecordingTab.Text = "녹화";
        btnPlaybackTab.Dock = DockStyle.Fill;
        btnPlaybackTab.Margin = new Padding(4, 0, 0, 0);
        btnPlaybackTab.Name = "btnPlaybackTab";
        btnPlaybackTab.Text = "재생";
        btnSettings.Dock = DockStyle.Fill;
        btnSettings.Margin = new Padding(6, 0, 0, 0);
        btnSettings.Name = "btnSettings";
        btnSettings.Text = "";
        btnSettings.AccessibleName = "설정";
        mainToolTip.SetToolTip(btnSettings, "설정");
        sideTabHeaderPanel.Controls.Add(btnRecordingTab, 0, 0);
        sideTabHeaderPanel.Controls.Add(btnPlaybackTab, 1, 0);
        sideTabHeaderPanel.Controls.Add(btnSettings, 2, 0);

        sideContentPanel.AutoScroll = true;
        sideContentPanel.Dock = DockStyle.Fill;
        sideContentPanel.FlowDirection = FlowDirection.TopDown;
        sideContentPanel.Name = "sideContentPanel";
        sideContentPanel.Padding = new Padding(10, 48, 6, 8);
        sideContentPanel.WrapContents = false;

        playbackTabPanel.BackColor = Color.Transparent;
        playbackTabPanel.Dock = DockStyle.Fill;
        playbackTabPanel.Name = "playbackTabPanel";
        playbackTabPanel.Padding = new Padding(10, 48, 6, 8);
        playbackTabPanel.Visible = false;

        statusStrip.Items.AddRange(new ToolStripItem[]
        {
            lblRtspStatus,
            lblLastFrame,
            lblFps,
            lblDetectionStatus,
            lblAlgorithmStatus,
            lblErrorStatus,
            lblVersion
        });
        statusStrip.Name = "statusStrip";
        statusStrip.SizingGrip = false;
        lblErrorStatus.Spring = true;
        lblErrorStatus.TextAlign = ContentAlignment.MiddleLeft;
        lblVersion.Text = "v1.0.0";

        ConfigureCameraControls();
        ConfigureOverlayControls();
        ConfigureThresholdControls();
        ConfigureRecordingControls();
        ConfigureStatusLabels();

        ConfigureCardValue(lblCameraCardState, "연결 상태    대기");
        ConfigureCardValue(lblCameraCardAddress, "카메라 주소  -");
        ConfigureCardValue(lblCameraCardStream, "스트림 상태  대기");
        ConfigureCardValue(lblRecordingCardState, "상태       녹화 꺼짐");
        ConfigureCardValue(lblRecordingCardFile, "현재 파일  -");
        ConfigureCardValue(lblRecordingCardElapsed, "경과 시간  00:00:00");
        ConfigureCardValue(lblRecordingCardLocation, "저장 위치  -");
        ConfigureCardValue(lblStorageCardDrive, "드라이브  확인 중");
        ConfigureCardValue(lblStorageCardUsage, "사용량    확인 중");
        storageUsageBar.Width = 374;
        storageUsageBar.Height = 8;
        storageUsageBar.Margin = new Padding(4, 4, 4, 8);

        var cameraCard = MakeCard("Settings.Camera",
            lblCameraCardState,
            lblCameraCardAddress,
            lblCameraCardStream,
            MakeActionRow(btnConnectCamera, btnDisconnectCamera),
            MakeActionRow(btnOpenCamera, btnCloseCamera));
        var recordingCard = MakeCard("Settings.Recording",
            MakeActionRow(rdoManualRecording, rdoAutoRecording, rdoFullRecording),
            lblRecordingCardState,
            lblRecordingCardFile,
            lblRecordingCardElapsed,
            lblRecordingCardLocation,
            MakeActionRow(btnStartRecording, btnStopRecording));
        var storageCard = MakeCard("Settings.Storage",
            lblStorageCardDrive,
            lblStorageCardUsage,
            storageUsageBar,
            MakeActionRow(btnOpenStorageFolder));
        sideContentPanel.Controls.Add(cameraCard);
        sideContentPanel.Controls.Add(recordingCard);
        sideContentPanel.Controls.Add(storageCard);

        legacyControlHost.Visible = false;
        legacyControlHost.Controls.AddRange(new Control[]
        {
            rdoIpCamera, rdoUsbCamera, cmbCameraList, cmbResolution, cmbFps, txtIpAddress,
            numRtspPort, numHttpPort, cmbStreamPath, chkUseManualRtspUrl, txtManualRtspUrl,
            txtGeneratedRtspUrl, btnRefreshCamera, btnApplyCamera, btnDefaultSettings,
            btnCameraSettings, btnRecordingSettings, btnStorageSettings, btnOpenEventList,
            btnWatchToggle, btnSaveHomeReference, btnCameraProperty,
            chkShowPersonBox, chkShowMotionMask, chkShowRodRoi, chkShowHomeRoi,
            chkShowDebugText, chkShowRecordingStatus, chkShowFrameTime,
            numMotionThreshold, numMinMotionArea, numPersonThreshold, numRodThreshold,
            numStrongRodThreshold, numHomeDiffThreshold, numHomeMotionThreshold
        });
        var playbackTabLayout = new TableLayoutPanel
        {
            BackColor = Color.Transparent,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            RowCount = 2
        };
        playbackTabLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        playbackTabLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
        playbackTabLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        btnLoadVideoFile.Dock = DockStyle.Fill;
        btnLoadVideoFile.Margin = new Padding(0, 4, 0, 8);
        playbackPanel.Controls.Add(playbackControl);
        playbackTabLayout.Controls.Add(btnLoadVideoFile, 0, 0);
        playbackTabLayout.Controls.Add(playbackPanel, 0, 1);
        playbackTabPanel.Controls.Add(playbackTabLayout);
        videoPanel.Controls.Add(picCameraPreview);
        videoPanel.Controls.Add(lblVideoCamera);
        videoPanel.Controls.Add(lblVideoInfo);
        videoPanel.Controls.Add(lblVideoRecording);
        mainContentPanel.Controls.Add(videoPanel);
        mainContentPanel.Controls.Add(sidePanel);
        mainContentPanel.Controls.Add(legacyControlHost);
        sidePanel.Controls.Add(sideContentPanel);
        sidePanel.Controls.Add(playbackTabPanel);
        sidePanel.Controls.Add(sideTabHeaderPanel);

        Controls.Add(mainContentPanel);
        Controls.Add(statusStrip);
        Controls.Add(topHeaderPanel);
        Controls.Add(menuStrip);

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1440, 810);
        KeyPreview = true;
        Location = new Point(10, 10);
        MainMenuStrip = menuStrip;
        MinimumSize = new Size(1280, 730);
        Name = "MainForm";
        StartPosition = FormStartPosition.Manual;
        Text = "DFBlackbox";
        ((System.ComponentModel.ISupportInitialize)picCameraPreview).EndInit();
        ResumeLayout(false);
        PerformLayout();
    }

    private void ConfigureCameraControls()
    {
        rdoIpCamera.Text = "IP 카메라";
        rdoIpCamera.Checked = true;
        rdoIpCamera.AutoSize = true;
        rdoUsbCamera.Text = "USB 카메라";
        rdoUsbCamera.AutoSize = true;

        cmbCameraList.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbResolution.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbResolution.Items.AddRange(new object[] { "320x240", "640x480", "800x600", "1280x720", "1920x1080" });
        cmbFps.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbFps.Items.AddRange(new object[] { "60", "30", "15", "10", "5" });

        numRtspPort.Minimum = 1;
        numRtspPort.Maximum = 65535;
        numRtspPort.Value = 554;
        numHttpPort.Minimum = 1;
        numHttpPort.Maximum = 65535;
        numHttpPort.Value = 80;
        cmbStreamPath.DropDownStyle = ComboBoxStyle.DropDown;
        cmbStreamPath.Items.AddRange(new object[]
        {
            "/stream1",
            "/live",
            "/h264",
            "/ch0_0.264",
            "/Streaming/Channels/101",
            "/cam/realmonitor?channel=1&subtype=0"
        });
        chkUseManualRtspUrl.Text = "수동 RTSP URL 사용";
        chkUseManualRtspUrl.AutoSize = true;
        txtGeneratedRtspUrl.ReadOnly = true;

        btnRefreshCamera.Text = "카메라 새로고침";
        btnApplyCamera.Text = "저장";
        btnWatchToggle.Text = "감시 시작";
        btnDefaultSettings.Text = "기본값";
        btnConnectCamera.Text = "연결";
        btnDisconnectCamera.Text = "연결 해제";
        btnOpenCamera.Text = "카메라 열기";
        btnCloseCamera.Text = "카메라 닫기";
        btnLoadVideoFile.Text = "영상 불러오기";
        btnCameraProperty.Text = "F1";
        btnSettings.Text = "설정";
        btnCameraSettings.Text = "카메라 설정";
        btnRecordingSettings.Text = "녹화 설정";
        btnStorageSettings.Text = "저장소 설정";

        SetPanelControlWidth(cmbCameraList);
        SetPanelControlWidth(cmbResolution);
        SetPanelControlWidth(cmbFps);
        btnRefreshCamera.Width = 88;
        btnApplyCamera.Width = 230;
        SetPanelControlWidth(btnWatchToggle);
        btnDefaultSettings.Width = 230;
        btnConnectCamera.Width = 230;
        btnDisconnectCamera.Width = 230;
        btnOpenCamera.Width = 230;
        btnCloseCamera.Width = 230;
        SetPanelControlWidth(btnLoadVideoFile);
        btnCameraProperty.Width = 48;
        btnSettings.Width = 230;
    }

    private void ConfigureOverlayControls()
    {
        chkShowPersonBox.Text = "움직임 박스";
        chkShowMotionMask.Text = "움직임 박스";
        chkShowRodRoi.Text = "ROI / 제외 ROI";
        chkShowHomeRoi.Text = "ROI";
        chkShowDebugText.Text = "디버그 텍스트";
        chkShowRecordingStatus.Text = "녹화 상태";
        chkShowFrameTime.Text = "프레임 시간";
        foreach (var checkBox in new[] { chkShowPersonBox, chkShowMotionMask, chkShowRodRoi, chkShowHomeRoi, chkShowDebugText, chkShowRecordingStatus, chkShowFrameTime })
        {
            checkBox.AutoSize = true;
        }
    }

    private void ConfigureThresholdControls()
    {
        numMotionThreshold.Minimum = 1;
        numMotionThreshold.Maximum = 255;
        numMotionThreshold.Value = 25;
        numMinMotionArea.Minimum = 1;
        numMinMotionArea.Maximum = 100000;
        numMinMotionArea.Value = 500;

        foreach (var input in new[] { numPersonThreshold, numRodThreshold, numStrongRodThreshold, numHomeMotionThreshold })
        {
            input.DecimalPlaces = 3;
            input.Increment = 0.005M;
            input.Minimum = 0;
            input.Maximum = 1;
        }

        numPersonThreshold.Value = 0.05M;
        numRodThreshold.Value = 0.02M;
        numStrongRodThreshold.Value = 0.08M;
        numHomeMotionThreshold.Value = 0.01M;
        numHomeDiffThreshold.DecimalPlaces = 1;
        numHomeDiffThreshold.Increment = 0.5M;
        numHomeDiffThreshold.Minimum = 0;
        numHomeDiffThreshold.Maximum = 255;
        numHomeDiffThreshold.Value = 18.0M;
    }

    private void ConfigureRecordingControls()
    {
        btnOpenStorageFolder.Text = "저장소";
        rdoManualRecording.Text = "수동";
        rdoManualRecording.Checked = true;
        rdoManualRecording.AutoSize = true;
        rdoAutoRecording.Text = "자동";
        rdoAutoRecording.AutoSize = true;
        rdoFullRecording.Text = "전체";
        rdoFullRecording.AutoSize = true;
        btnStartRecording.Text = "녹화 시작";
        btnStopRecording.Text = "녹화 정지";
        btnSaveHomeReference.Text = "차이 기준 저장";
        btnOpenEventList.Text = "최근 이벤트";
        btnOpenStorageFolder.Width = 230;
        btnStartRecording.Width = 230;
        btnStopRecording.Width = 230;
        SetPanelControlWidth(btnSaveHomeReference);
        SetPanelControlWidth(btnOpenEventList);
    }

    private void ConfigureStatusLabels()
    {
        lblCameraStatus.Text = "카메라: 대기";
        lblRtspStatus.Text = "RTSP: 해당 없음";
        lblLastFrame.Text = "마지막 프레임: 없음";
        lblFps.Text = "FPS: 0";
        lblDetectionStatus.Text = "감지: 대기";
        lblAlgorithmStatus.Text = "알고리즘: 꺼짐";
        lblRecordingStatus.Text = "녹화: 꺼짐";
        lblDiskStatus.Text = "디스크: 알 수 없음";
        lblErrorStatus.Text = "오류: 없음";
    }

    private static void ConfigureHeaderLabel(Label label, string text, ContentAlignment alignment, bool title = false)
    {
        label.AutoEllipsis = true;
        label.BackColor = Color.Transparent;
        label.Dock = DockStyle.Fill;
        label.Font = new Font("Segoe UI", title ? 11F : 9.5F, FontStyle.Bold);
        label.ForeColor = Color.White;
        label.Margin = Padding.Empty;
        label.Text = text;
        label.TextAlign = alignment;
    }

    private static void ConfigureVideoOverlayLabel(Label label, string text)
    {
        label.AutoSize = true;
        label.BackColor = Color.Black;
        label.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        label.ForeColor = Color.White;
        label.Padding = new Padding(7, 4, 7, 4);
        label.Text = text;
    }

    private static void ConfigureCardValue(Label label, string text)
    {
        label.AutoEllipsis = true;
        label.Font = new Font("Segoe UI", 9F, FontStyle.Regular);
        label.ForeColor = Color.FromArgb(67, 77, 98);
        label.Height = 24;
        label.Margin = new Padding(4, 1, 4, 1);
        label.Text = text;
        label.TextAlign = ContentAlignment.MiddleLeft;
        label.Width = 374;
    }

    private static FlowLayoutPanel MakeCard(string titleKey, params Control[] controls)
    {
        var card = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            FlowDirection = FlowDirection.TopDown,
            Margin = new Padding(0, 8, 0, 0),
            Padding = new Padding(8),
            Width = 396,
            WrapContents = false
        };
        card.Controls.Add(new Label
        {
            AutoSize = false,
            Font = new Font("Segoe UI", 10.5F, FontStyle.Bold),
            ForeColor = Color.FromArgb(28, 43, 67),
            Height = 28,
            Margin = new Padding(4, 0, 4, 4),
            Tag = titleKey,
            Text = DFBlackbox.Utils.Localization.T(titleKey),
            TextAlign = ContentAlignment.MiddleLeft,
            Width = 374
        });

        foreach (Control control in controls)
        {
            card.Controls.Add(control);
        }

        return card;
    }

    private static FlowLayoutPanel MakeActionRow(params Control[] controls)
    {
        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            Height = 42,
            Margin = new Padding(4, 3, 4, 3),
            Width = 374,
            WrapContents = false
        };
        int controlWidth = controls.Length <= 1 ? 370 : (370 - (controls.Length - 1) * 6) / controls.Length;
        foreach (Control control in controls)
        {
            control.Height = 34;
            control.Margin = new Padding(0, 3, 6, 3);
            control.Width = controlWidth;
            row.Controls.Add(control);
        }

        return row;
    }

    private static Label MakeHeader(string key) => new()
    {
        Text = DFBlackbox.Utils.Localization.T(key),
        Tag = key,
        AutoSize = true,
        Font = new Font("Segoe UI", 10F, FontStyle.Bold),
        Margin = new Padding(0, 4, 0, 4)
    };

    private static Label MakeSectionLabel(string key) => new()
    {
        Text = DFBlackbox.Utils.Localization.T(key),
        Tag = key,
        AutoSize = true,
        Font = new Font("Segoe UI", 9F, FontStyle.Bold),
        Margin = new Padding(0, 14, 0, 3)
    };

    private static Panel MakeLabeledPanel(string label, Control control)
    {
        var panel = new Panel { Width = 210, Height = 48, Margin = new Padding(0, 4, 0, 4) };
        panel.Controls.Add(new Label { Text = label, Dock = DockStyle.Top, Height = 18 });
        control.Dock = DockStyle.Bottom;
        panel.Controls.Add(control);
        return panel;
    }

    private static void SetPanelControlWidth(Control control)
    {
        control.Width = 464;
        control.Margin = new Padding(0, 5, 0, 5);
    }

    private static FlowLayoutPanel MakeRowPanel(params Control[] controls)
    {
        var panel = new FlowLayoutPanel
        {
            Width = 474,
            Height = 40,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 3, 0, 3)
        };

        foreach (var control in controls)
        {
            control.Margin = new Padding(0, 4, 4, 4);
            panel.Controls.Add(control);
        }

        return panel;
    }

    private static Panel MakeBottomSpacer() => new()
    {
        Width = 474,
        Height = 24,
        Margin = new Padding(0, 8, 0, 2)
    };

    private static Panel MakeMiniLabeled(string label, Control control, int width)
    {
        var panel = new Panel { Width = width, Height = 28, Margin = new Padding(0, 0, 4, 0) };
        control.Dock = DockStyle.Fill;
        panel.Controls.Add(control);
        panel.Controls.Add(new Label { Text = label, Dock = DockStyle.Left, Width = Math.Min(100, width / 2), TextAlign = ContentAlignment.MiddleLeft });
        return panel;
    }
}
