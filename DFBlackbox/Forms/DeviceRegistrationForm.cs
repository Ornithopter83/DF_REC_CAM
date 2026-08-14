using System.Diagnostics;
using DFBlackbox.Core;
using DFBlackbox.Models;
using DFBlackbox.Utils;
using Krypton.Toolkit;
using QRCoder;

namespace DFBlackbox.Forms;

public sealed class DeviceRegistrationForm : KryptonForm
{
    private readonly DeviceRegistrationSettings _settings;
    private readonly CreateDeviceClaimRequest _request;
    private readonly DeviceRegistrationWorkflow _workflow;
    private readonly Action<DeviceRegistrationResult>? _registrationCompleted;
    private readonly IDisposable? _ownedClient;
    private readonly IDisposable? _ownedNasProvisioning;
    private readonly Label _status = new();
    private readonly PictureBox _qrCode = new();
    private readonly Panel _qrHost = new();
    private readonly Label _claimCode = new();
    private readonly Label _expiresAt = new();
    private readonly Button _start = new();
    private readonly Button _openApproval = new();
    private readonly Button _copyCode = new();
    private readonly Button _retryStorage = new();
    private readonly Button _cancel = new();
    private CancellationTokenSource? _registrationCancellation;
    private string? _approvalUrl;
    private bool _closing;
    private bool _completed;

    public DeviceRegistrationForm(
        DeviceRegistrationSettings settings,
        CreateDeviceClaimRequest request,
        IDeviceRegistrationClient client,
        IDeviceTokenStore tokenStore,
        INasProvisioningService nasProvisioning,
        Action<DeviceRegistrationResult>? registrationCompleted = null)
        : this(
            settings,
            request,
            client,
            new DeviceRegistrationWorkflow(client, tokenStore, nasProvisioning),
            nasProvisioning as IDisposable,
            registrationCompleted)
    {
    }

    public DeviceRegistrationForm(
        DeviceRegistrationSettings settings,
        CreateDeviceClaimRequest request,
        IDeviceRegistrationClient client,
        IDeviceTokenStore tokenStore,
        INasHttpsProvisioningService nasProvisioning,
        Action<DeviceRegistrationResult>? registrationCompleted = null)
        : this(
            settings,
            request,
            client,
            new DeviceRegistrationWorkflow(client, tokenStore, nasProvisioning),
            nasProvisioning as IDisposable,
            registrationCompleted)
    {
    }

    private DeviceRegistrationForm(
        DeviceRegistrationSettings settings,
        CreateDeviceClaimRequest request,
        IDeviceRegistrationClient client,
        DeviceRegistrationWorkflow workflow,
        IDisposable? ownedNasProvisioning,
        Action<DeviceRegistrationResult>? registrationCompleted)
    {
        _settings = settings;
        _request = request;
        _workflow = workflow;
        _registrationCompleted = registrationCompleted;
        _ownedClient = client as IDisposable;
        _ownedNasProvisioning = ownedNasProvisioning;

        Text = Localization.T("Registration.Title");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(800, 680);
        Build();
        UiTheme.ApplyFormTheme(this);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _closing = true;
        _registrationCancellation?.Cancel();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _registrationCancellation?.Cancel();
            _registrationCancellation?.Dispose();
            ReplaceQrCode(null);
            _ownedClient?.Dispose();
            _ownedNasProvisioning?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void Build()
    {
        var content = new FlowLayoutPanel
        {
            AutoScroll = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(24),
            WrapContents = false
        };
        content.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 15F, FontStyle.Bold),
            Text = Localization.T("Registration.Title")
        });
        content.Controls.Add(new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 18),
            MaximumSize = new Size(580, 0),
            Text = Localization.T("Registration.FlowDescription")
        });

        _status.AutoSize = true;
        _status.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        _status.Margin = new Padding(0, 4, 0, 14);
        _status.Text = Localization.T("Registration.Ready");
        content.Controls.Add(_status);

        _qrHost.Height = 248;
        _qrHost.Margin = new Padding(0, 0, 0, 12);
        _qrHost.Width = 736;
        _qrCode.BackColor = Color.White;
        _qrCode.Location = new Point(248, 0);
        _qrCode.Size = new Size(240, 240);
        _qrCode.SizeMode = PictureBoxSizeMode.Zoom;
        _qrCode.Visible = false;
        _qrHost.Controls.Add(_qrCode);
        content.Controls.Add(_qrHost);

        _claimCode.AutoSize = true;
        _claimCode.Font = new Font("Consolas", 20F, FontStyle.Bold);
        _claimCode.Margin = new Padding(0, 4, 0, 8);
        _claimCode.Text = "-";
        content.Controls.Add(_claimCode);

        _expiresAt.AutoSize = true;
        _expiresAt.Margin = new Padding(0, 0, 0, 18);
        _expiresAt.Text = Localization.T("Registration.ExpiresNone");
        content.Controls.Add(_expiresAt);

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = Padding.Empty,
            WrapContents = false
        };
        ConfigureButton(_start, Localization.T("Registration.Start"), async (_, _) => await StartRegistrationAsync());
        ConfigureButton(_openApproval, Localization.T("Registration.OpenApproval"), (_, _) => OpenApprovalPage());
        ConfigureButton(_copyCode, Localization.T("Registration.CopyCode"), (_, _) => CopyClaimCode());
        ConfigureButton(_retryStorage, Localization.T("Registration.RetryStorage"), async (_, _) => await RetryStorageAsync());
        ConfigureButton(_cancel, Localization.T("Button.Cancel"), (_, _) => CancelOrClose());
        _openApproval.Enabled = false;
        _copyCode.Enabled = false;
        _retryStorage.Enabled = HasRegisteredDevice();
        _cancel.Enabled = false;
        actions.Controls.AddRange(new Control[] { _start, _openApproval, _copyCode, _retryStorage, _cancel });
        content.Controls.Add(actions);
        Controls.Add(content);
    }

    private async Task StartRegistrationAsync()
    {
        CancelRegistration();
        _registrationCancellation?.Dispose();
        _registrationCancellation = new CancellationTokenSource();
        _completed = false;
        _qrHost.Visible = true;
        _claimCode.Visible = true;
        _expiresAt.Visible = true;
        _start.Visible = true;
        _openApproval.Visible = true;
        _copyCode.Visible = true;
        _retryStorage.Visible = true;
        _cancel.Text = Localization.T("Button.Cancel");
        _approvalUrl = null;
        ReplaceQrCode(null);
        _claimCode.Text = "-";
        _expiresAt.Text = Localization.T("Registration.ExpiresNone");
        SetRunning(running: true);

        var progress = new Progress<DeviceRegistrationProgress>(UpdateProgress);
        try
        {
            DeviceRegistrationResult result = await _workflow.RunAsync(
                _settings,
                _request,
                progress,
                _registrationCancellation.Token);
            if (!string.IsNullOrWhiteSpace(result.DeviceId))
            {
                _registrationCompleted?.Invoke(result);
                _retryStorage.Enabled = true;
                if (result.Stage == DeviceRegistrationStage.Completed)
                {
                    ShowCompletedState(result);
                }
            }
        }
        catch (OperationCanceledException)
        {
            if (!_closing)
            {
                _status.Text = Localization.T("Registration.Cancelled");
            }
        }
        catch (Exception ex)
        {
            if (!_closing)
            {
                _status.Text = Localization.T("Registration.Failed", ex.Message);
            }
        }
        finally
        {
            if (!_closing)
            {
                SetRunning(running: false);
            }
        }
    }

    private void UpdateProgress(DeviceRegistrationProgress progress)
    {
        if (_closing || IsDisposed)
        {
            return;
        }

        if (progress.Claim is not null)
        {
            _claimCode.Text = progress.Claim.ClaimCode;
            _approvalUrl = progress.Claim.ApprovalUrl;
            ShowApprovalQrCode(progress.Claim.ApprovalUrl);
            _expiresAt.Text = Localization.T(
                "Registration.ExpiresAt",
                progress.Claim.ExpiresAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
            _openApproval.Enabled = true;
            _copyCode.Enabled = true;
        }

        _status.Text = progress.Stage switch
        {
            DeviceRegistrationStage.CreatingClaim => Localization.T("Registration.Creating"),
            DeviceRegistrationStage.WaitingForApproval => Localization.T("Registration.Waiting"),
            DeviceRegistrationStage.Approved => Localization.T("Registration.Approved"),
            DeviceRegistrationStage.ProvisioningStorage => Localization.T("Registration.Provisioning"),
            DeviceRegistrationStage.Completed => Localization.T("Registration.Completed"),
            DeviceRegistrationStage.Rejected => Localization.T("Registration.Rejected"),
            DeviceRegistrationStage.Expired => Localization.T("Registration.Expired"),
            DeviceRegistrationStage.StorageError => Localization.T("Registration.StorageError", progress.ErrorCode ?? "unknown"),
            DeviceRegistrationStage.ReportError => Localization.T("Registration.ReportError"),
            _ => _status.Text
        };
    }

    private async Task RetryStorageAsync()
    {
        CancelRegistration();
        _registrationCancellation?.Dispose();
        _registrationCancellation = new CancellationTokenSource();
        SetRunning(running: true);
        var progress = new Progress<DeviceRegistrationProgress>(UpdateProgress);
        try
        {
            DeviceRegistrationResult result = await _workflow.RetryProvisioningAsync(
                _settings,
                progress,
                _registrationCancellation.Token);
            _registrationCompleted?.Invoke(result);
            if (result.Stage == DeviceRegistrationStage.Completed)
            {
                ShowCompletedState(result);
            }
        }
        catch (OperationCanceledException)
        {
            if (!_closing)
            {
                _status.Text = Localization.T("Registration.Cancelled");
            }
        }
        catch (Exception ex)
        {
            if (!_closing)
            {
                _status.Text = Localization.T("Registration.Failed", ex.Message);
            }
        }
        finally
        {
            if (!_closing)
            {
                SetRunning(running: false);
            }
        }
    }

    private void OpenApprovalPage()
    {
        if (_approvalUrl is null
            || !Uri.TryCreate(_approvalUrl, UriKind.Absolute, out Uri? uri)
            || uri.Scheme is not ("http" or "https"))
        {
            return;
        }

        Process.Start(new ProcessStartInfo { FileName = uri.AbsoluteUri, UseShellExecute = true });
    }

    private void CopyClaimCode()
    {
        if (_claimCode.Text != "-")
        {
            Clipboard.SetText(_claimCode.Text);
        }
    }

    private void CancelRegistration()
    {
        _registrationCancellation?.Cancel();
    }

    private void CancelOrClose()
    {
        if (_completed)
        {
            Close();
            return;
        }

        CancelRegistration();
    }

    private void ShowCompletedState(DeviceRegistrationResult result)
    {
        _completed = true;
        _approvalUrl = null;
        ReplaceQrCode(null);
        _qrHost.Visible = false;
        _claimCode.Visible = true;
        _claimCode.Font = new Font("Segoe UI", 16F, FontStyle.Bold);
        _claimCode.Text = Localization.T(
            "Registration.CompletedAs",
            string.IsNullOrWhiteSpace(result.RegistrationName)
                ? Environment.MachineName
                : result.RegistrationName);
        _expiresAt.Visible = true;
        _expiresAt.Text = Localization.T("Registration.NasPath", result.NasRelativePath ?? "-");
        _status.Text = Localization.T("Registration.Completed");
        _start.Visible = false;
        _openApproval.Visible = false;
        _copyCode.Visible = false;
        _retryStorage.Visible = false;
        _cancel.Visible = true;
        _cancel.Enabled = true;
        _cancel.Text = Localization.T("Button.Close");
    }

    private void ShowApprovalQrCode(string approvalUrl)
    {
        try
        {
            using QRCodeData data = QRCodeGenerator.GenerateQrCode(
                approvalUrl,
                QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new QRCode(data);
            ReplaceQrCode(qrCode.GetGraphic(5, Color.Black, Color.White, drawQuietZones: true));
        }
        catch
        {
            // QR 렌더링 실패가 등록/폴링 흐름을 중단하지 않게 한다.
            ReplaceQrCode(null);
        }
    }

    private void ReplaceQrCode(Image? image)
    {
        Image? previous = _qrCode.Image;
        _qrCode.Image = image;
        _qrCode.Visible = image is not null;
        previous?.Dispose();
    }

    private void SetRunning(bool running)
    {
        if (_completed)
        {
            return;
        }

        _start.Enabled = !running;
        _retryStorage.Enabled = !running && HasRegisteredDevice();
        _cancel.Enabled = running;
    }

    private bool HasRegisteredDevice() =>
        !string.IsNullOrWhiteSpace(_settings.DeviceId)
        && !string.IsNullOrWhiteSpace(_settings.CameraId)
        && !string.IsNullOrWhiteSpace(_settings.NasRelativePath);

    private static void ConfigureButton(Button button, string text, EventHandler click)
    {
        button.Text = text;
        button.Width = 132;
        button.Height = 36;
        button.Margin = new Padding(0, 4, 8, 4);
        button.Click += click;
    }
}
