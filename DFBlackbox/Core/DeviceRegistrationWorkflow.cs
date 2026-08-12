using DFBlackbox.Models;

namespace DFBlackbox.Core;

public sealed class DeviceRegistrationWorkflow
{
    private readonly IDeviceRegistrationClient _client;
    private readonly IDeviceTokenStore _tokenStore;
    private readonly INasProvisioningService _nasProvisioning;

    public DeviceRegistrationWorkflow(
        IDeviceRegistrationClient client,
        IDeviceTokenStore tokenStore,
        INasProvisioningService nasProvisioning)
    {
        _client = client;
        _tokenStore = tokenStore;
        _nasProvisioning = nasProvisioning;
    }

    public async Task<DeviceRegistrationResult> RunAsync(
        DeviceRegistrationSettings settings,
        CreateDeviceClaimRequest request,
        IProgress<DeviceRegistrationProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new DeviceRegistrationProgress(DeviceRegistrationStage.CreatingClaim));
        CreateDeviceClaimResponse claim = await _client.CreateClaimAsync(request, cancellationToken);
        ValidateClaim(claim);
        progress?.Report(new DeviceRegistrationProgress(DeviceRegistrationStage.WaitingForApproval, claim));

        DateTimeOffset configuredDeadline = DateTimeOffset.UtcNow.AddMinutes(
            Math.Clamp(settings.ClaimTimeoutMinutes, 1, 30));
        DateTimeOffset deadline = claim.ExpiresAt < configuredDeadline ? claim.ExpiresAt : configuredDeadline;
        TimeSpan pollInterval = TimeSpan.FromSeconds(Math.Clamp(settings.PollIntervalSeconds, 2, 10));

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeviceClaimStatusResponse status = await _client.GetClaimStatusAsync(
                claim.ClaimId,
                cancellationToken);
            switch (NormalizeStatus(status.Status))
            {
                case "approved":
                    return await CompleteApprovedClaimAsync(settings, status, progress, cancellationToken);
                case "rejected":
                    progress?.Report(new DeviceRegistrationProgress(DeviceRegistrationStage.Rejected, claim));
                    return new DeviceRegistrationResult(DeviceRegistrationStage.Rejected);
                case "expired":
                    progress?.Report(new DeviceRegistrationProgress(DeviceRegistrationStage.Expired, claim));
                    return new DeviceRegistrationResult(DeviceRegistrationStage.Expired);
                case "pending":
                    break;
                default:
                    throw new InvalidDataException("The registration server returned an unknown claim status.");
            }

            TimeSpan remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            await Task.Delay(remaining < pollInterval ? remaining : pollInterval, cancellationToken);
        }

        progress?.Report(new DeviceRegistrationProgress(DeviceRegistrationStage.Expired, claim));
        return new DeviceRegistrationResult(DeviceRegistrationStage.Expired);
    }

    public async Task<DeviceRegistrationResult> RetryProvisioningAsync(
        DeviceRegistrationSettings settings,
        IProgress<DeviceRegistrationProgress>? progress,
        CancellationToken cancellationToken)
    {
        string deviceId = Require(settings.DeviceId, "device_id");
        string cameraId = Require(settings.CameraId, "camera_id");
        string relativePath = Require(settings.NasRelativePath, "nas_relative_path");
        string? deviceToken = await _tokenStore.LoadAsync(deviceId, cancellationToken);
        if (string.IsNullOrWhiteSpace(deviceToken))
        {
            throw new InvalidDataException("No encrypted token is available for the registered device.");
        }

        return await ProvisionAndReportAsync(
            settings,
            deviceId,
            cameraId,
            relativePath,
            registrationName: null,
            deviceToken,
            progress,
            cancellationToken);
    }

    private async Task<DeviceRegistrationResult> CompleteApprovedClaimAsync(
        DeviceRegistrationSettings settings,
        DeviceClaimStatusResponse status,
        IProgress<DeviceRegistrationProgress>? progress,
        CancellationToken cancellationToken)
    {
        string deviceId = Require(status.DeviceId, "device_id");
        string cameraId = Require(status.CameraId, "camera_id");
        string relativePath = Require(status.NasRelativePath, "nas_relative_path");
        progress?.Report(new DeviceRegistrationProgress(DeviceRegistrationStage.Approved));

        string? deviceToken = status.DeviceToken;
        if (!string.IsNullOrWhiteSpace(deviceToken))
        {
            await _tokenStore.SaveOnceAsync(deviceId, deviceToken, cancellationToken);
        }
        else
        {
            deviceToken = await _tokenStore.LoadAsync(deviceId, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(deviceToken))
        {
            throw new InvalidDataException("The approved claim did not include the one-time device token.");
        }

        return await ProvisionAndReportAsync(
            settings,
            deviceId,
            cameraId,
            relativePath,
            status.RegistrationName,
            deviceToken,
            progress,
            cancellationToken);
    }

    private async Task<DeviceRegistrationResult> ProvisionAndReportAsync(
        DeviceRegistrationSettings settings,
        string deviceId,
        string cameraId,
        string relativePath,
        string? registrationName,
        string deviceToken,
        IProgress<DeviceRegistrationProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new DeviceRegistrationProgress(DeviceRegistrationStage.ProvisioningStorage));
        NasProvisioningResult storage = await _nasProvisioning.ProvisionAsync(
            settings.NasRootFolder,
            relativePath,
            cancellationToken);
        var report = new ProvisioningResultRequest(
            "active",
            storage.IsReady ? "ready" : "storage_error",
            storage.ErrorCode);
        try
        {
            await _client.ReportProvisioningResultAsync(
                deviceId,
                deviceToken,
                report,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            const string reportError = "provisioning_report_failed";
            progress?.Report(new DeviceRegistrationProgress(DeviceRegistrationStage.ReportError, ErrorCode: reportError));
            return new DeviceRegistrationResult(
                DeviceRegistrationStage.ReportError,
                deviceId,
                cameraId,
                relativePath,
                registrationName,
                reportError);
        }

        DeviceRegistrationStage stage = storage.IsReady
            ? DeviceRegistrationStage.Completed
            : DeviceRegistrationStage.StorageError;
        progress?.Report(new DeviceRegistrationProgress(stage, ErrorCode: storage.ErrorCode));
        return new DeviceRegistrationResult(
            stage,
            deviceId,
            cameraId,
            relativePath,
            registrationName,
            storage.ErrorCode);
    }

    private static void ValidateClaim(CreateDeviceClaimResponse claim)
    {
        if (string.IsNullOrWhiteSpace(claim.ClaimId)
            || string.IsNullOrWhiteSpace(claim.ClaimCode)
            || !Uri.TryCreate(claim.ApprovalUrl, UriKind.Absolute, out Uri? approvalUri)
            || approvalUri.Scheme is not ("http" or "https")
            || claim.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw new InvalidDataException("The registration server returned an invalid claim.");
        }
    }

    private static string NormalizeStatus(string? status) => status?.Trim().ToLowerInvariant() ?? "";

    private static string Require(string? value, string fieldName) =>
        !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidDataException($"The approved claim is missing {fieldName}.");
}
