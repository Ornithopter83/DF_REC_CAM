namespace DFBlackbox.Forms;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null;
    private PictureBox picCameraPreview;
    private FlowLayoutPanel playbackPanel;
    private PlaybackControl playbackControl;
    private FlowLayoutPanel sidePanel;
    private StatusStrip statusStrip;
    private RadioButton rdoUsbCamera;
    private RadioButton rdoIpCamera;
    private ComboBox cmbCameraList;
    private ComboBox cmbResolution;
    private ComboBox cmbFps;
    private TextBox txtIpAddress;
    private NumericUpDown numRtspPort;
    private NumericUpDown numHttpPort;
    private ComboBox cmbStreamPath;
    private CheckBox chkUseManualRtspUrl;
    private TextBox txtManualRtspUrl;
    private TextBox txtGeneratedRtspUrl;
    private Button btnRefreshCamera;
    private Button btnApplyCamera;
    private Button btnWatchToggle;
    private Button btnDefaultSettings;
    private Button btnConnectCamera;
    private Button btnDisconnectCamera;
    private Button btnOpenCamera;
    private Button btnCloseCamera;
    private Button btnLoadVideoFile;
    private Button btnCameraProperty;
    private Button btnOpenStorageFolder;
    private Button btnSettings;
    private RadioButton rdoManualRecording;
    private RadioButton rdoAutoRecording;
    private RadioButton rdoFullRecording;
    private Button btnStartRecording;
    private Button btnStopRecording;
    private Button btnSaveHomeReference;
    private Button btnOpenEventList;
    private CheckBox chkShowPersonBox;
    private CheckBox chkShowMotionMask;
    private CheckBox chkShowRodRoi;
    private CheckBox chkShowHomeRoi;
    private CheckBox chkShowDebugText;
    private CheckBox chkShowRecordingStatus;
    private CheckBox chkShowFrameTime;
    private NumericUpDown numMotionThreshold;
    private NumericUpDown numMinMotionArea;
    private NumericUpDown numPersonThreshold;
    private NumericUpDown numRodThreshold;
    private NumericUpDown numStrongRodThreshold;
    private NumericUpDown numHomeDiffThreshold;
    private NumericUpDown numHomeMotionThreshold;
    private ToolStripStatusLabel lblCameraStatus;
    private ToolStripStatusLabel lblRtspStatus;
    private ToolStripStatusLabel lblLastFrame;
    private ToolStripStatusLabel lblFps;
    private ToolStripStatusLabel lblDetectionStatus;
    private ToolStripStatusLabel lblAlgorithmStatus;
    private ToolStripStatusLabel lblRecordingStatus;
    private ToolStripStatusLabel lblDiskStatus;
    private ToolStripStatusLabel lblErrorStatus;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null)
        {
            components.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        picCameraPreview = new PictureBox();
        playbackPanel = new FlowLayoutPanel();
        playbackControl = new PlaybackControl();
        sidePanel = new FlowLayoutPanel();
        statusStrip = new StatusStrip();
        rdoUsbCamera = new RadioButton();
        rdoIpCamera = new RadioButton();
        cmbCameraList = new ComboBox();
        cmbResolution = new ComboBox();
        cmbFps = new ComboBox();
        txtIpAddress = new TextBox();
        numRtspPort = new NumericUpDown();
        numHttpPort = new NumericUpDown();
        cmbStreamPath = new ComboBox();
        chkUseManualRtspUrl = new CheckBox();
        txtManualRtspUrl = new TextBox();
        txtGeneratedRtspUrl = new TextBox();
        btnRefreshCamera = new Button();
        btnApplyCamera = new Button();
        btnWatchToggle = new Button();
        btnDefaultSettings = new Button();
        btnConnectCamera = new Button();
        btnDisconnectCamera = new Button();
        btnOpenCamera = new Button();
        btnCloseCamera = new Button();
        btnLoadVideoFile = new Button();
        btnCameraProperty = new Button();
        btnOpenStorageFolder = new Button();
        btnSettings = new Button();
        rdoManualRecording = new RadioButton();
        rdoAutoRecording = new RadioButton();
        rdoFullRecording = new RadioButton();
        btnStartRecording = new Button();
        btnStopRecording = new Button();
        btnSaveHomeReference = new Button();
        btnOpenEventList = new Button();
        chkShowPersonBox = new CheckBox();
        chkShowMotionMask = new CheckBox();
        chkShowRodRoi = new CheckBox();
        chkShowHomeRoi = new CheckBox();
        chkShowDebugText = new CheckBox();
        chkShowRecordingStatus = new CheckBox();
        chkShowFrameTime = new CheckBox();
        numMotionThreshold = new NumericUpDown();
        numMinMotionArea = new NumericUpDown();
        numPersonThreshold = new NumericUpDown();
        numRodThreshold = new NumericUpDown();
        numStrongRodThreshold = new NumericUpDown();
        numHomeDiffThreshold = new NumericUpDown();
        numHomeMotionThreshold = new NumericUpDown();
        lblCameraStatus = new ToolStripStatusLabel();
        lblRtspStatus = new ToolStripStatusLabel();
        lblLastFrame = new ToolStripStatusLabel();
        lblFps = new ToolStripStatusLabel();
        lblDetectionStatus = new ToolStripStatusLabel();
        lblAlgorithmStatus = new ToolStripStatusLabel();
        lblRecordingStatus = new ToolStripStatusLabel();
        lblDiskStatus = new ToolStripStatusLabel();
        lblErrorStatus = new ToolStripStatusLabel();
        ((System.ComponentModel.ISupportInitialize)picCameraPreview).BeginInit();
        ((System.ComponentModel.ISupportInitialize)numRtspPort).BeginInit();
        ((System.ComponentModel.ISupportInitialize)numHttpPort).BeginInit();
        ((System.ComponentModel.ISupportInitialize)numMotionThreshold).BeginInit();
        ((System.ComponentModel.ISupportInitialize)numMinMotionArea).BeginInit();
        ((System.ComponentModel.ISupportInitialize)numPersonThreshold).BeginInit();
        ((System.ComponentModel.ISupportInitialize)numRodThreshold).BeginInit();
        ((System.ComponentModel.ISupportInitialize)numStrongRodThreshold).BeginInit();
        ((System.ComponentModel.ISupportInitialize)numHomeDiffThreshold).BeginInit();
        ((System.ComponentModel.ISupportInitialize)numHomeMotionThreshold).BeginInit();
        SuspendLayout();

        picCameraPreview.BackColor = Color.Black;
        picCameraPreview.Dock = DockStyle.Fill;
        picCameraPreview.Name = "picCameraPreview";
        picCameraPreview.SizeMode = PictureBoxSizeMode.Zoom;

        playbackPanel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        playbackPanel.BackColor = Color.Black;
        playbackPanel.FlowDirection = FlowDirection.LeftToRight;
        playbackPanel.Height = 150;
        playbackPanel.Location = new Point(804, 642);
        playbackPanel.Name = "playbackPanel";
        playbackPanel.Padding = new Padding(12);
        playbackPanel.Width = 452;
        playbackPanel.WrapContents = false;
        playbackPanel.Visible = false;

        playbackControl.Margin = new Padding(0);
        playbackControl.Name = "playbackControl";
        playbackControl.Size = new Size(428, 126);

        sidePanel.AutoScroll = true;
        sidePanel.Dock = DockStyle.Right;
        sidePanel.FlowDirection = FlowDirection.TopDown;
        sidePanel.Name = "sidePanel";
        sidePanel.Padding = new Padding(8);
        sidePanel.Width = 500;
        sidePanel.WrapContents = false;

        statusStrip.Items.AddRange(new ToolStripItem[]
        {
            lblCameraStatus,
            lblRtspStatus,
            lblLastFrame,
            lblFps,
            lblDetectionStatus,
            lblAlgorithmStatus,
            lblRecordingStatus,
            lblDiskStatus,
            lblErrorStatus
        });
        statusStrip.Name = "statusStrip";

        ConfigureCameraControls();
        ConfigureOverlayControls();
        ConfigureThresholdControls();
        ConfigureRecordingControls();
        ConfigureStatusLabels();

        sidePanel.Controls.Add(MakeHeader("카메라"));
        sidePanel.Controls.Add(MakeRowPanel(btnConnectCamera, btnDisconnectCamera));
        sidePanel.Controls.Add(MakeRowPanel(btnOpenCamera, btnCloseCamera));
        sidePanel.Controls.Add(btnLoadVideoFile);
        sidePanel.Controls.Add(btnSaveHomeReference);
        sidePanel.Controls.Add(btnWatchToggle);
        sidePanel.Controls.Add(MakeSectionLabel("녹화 / 저장소"));
        sidePanel.Controls.Add(MakeRowPanel(rdoManualRecording, rdoAutoRecording, rdoFullRecording));
        sidePanel.Controls.Add(MakeRowPanel(btnStartRecording, btnStopRecording));
        sidePanel.Controls.Add(MakeRowPanel(btnOpenStorageFolder, btnSettings));
        sidePanel.Controls.Add(btnOpenEventList);
        sidePanel.Controls.Add(MakeBottomSpacer());
        sidePanel.Controls.Add(MakeRowPanel(btnApplyCamera, btnDefaultSettings));

        playbackPanel.Controls.Add(playbackControl);

        Controls.Add(picCameraPreview);
        Controls.Add(sidePanel);
        Controls.Add(playbackPanel);
        Controls.Add(statusStrip);

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1140, 502);
        KeyPreview = true;
        Location = new Point(10, 10);
        Name = "MainForm";
        StartPosition = FormStartPosition.Manual;
        Text = "DFBlackbox";
        ((System.ComponentModel.ISupportInitialize)picCameraPreview).EndInit();
        ((System.ComponentModel.ISupportInitialize)numRtspPort).EndInit();
        ((System.ComponentModel.ISupportInitialize)numHttpPort).EndInit();
        ((System.ComponentModel.ISupportInitialize)numMotionThreshold).EndInit();
        ((System.ComponentModel.ISupportInitialize)numMinMotionArea).EndInit();
        ((System.ComponentModel.ISupportInitialize)numPersonThreshold).EndInit();
        ((System.ComponentModel.ISupportInitialize)numRodThreshold).EndInit();
        ((System.ComponentModel.ISupportInitialize)numStrongRodThreshold).EndInit();
        ((System.ComponentModel.ISupportInitialize)numHomeDiffThreshold).EndInit();
        ((System.ComponentModel.ISupportInitialize)numHomeMotionThreshold).EndInit();
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

    private static Label MakeHeader(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font("Segoe UI", 10F, FontStyle.Bold),
        Margin = new Padding(0, 4, 0, 4)
    };

    private static Label MakeSectionLabel(string text) => new()
    {
        Text = text,
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
        control.Margin = new Padding(0, 4, 0, 4);
    }

    private static FlowLayoutPanel MakeRowPanel(params Control[] controls)
    {
        var panel = new FlowLayoutPanel
        {
            Width = 474,
            Height = 30,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 2, 0, 2)
        };

        foreach (var control in controls)
        {
            control.Margin = new Padding(0, 2, 4, 2);
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
