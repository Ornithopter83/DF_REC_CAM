using System.Text.Json.Serialization;

namespace DFBlackbox.Models;

public sealed record CreateDeviceClaimRequest(
    [property: JsonPropertyName("app_version")] string AppVersion,
    [property: JsonPropertyName("installation_id")] string InstallationId,
    [property: JsonPropertyName("camera_type")] string CameraType);

public sealed record CreateDeviceClaimResponse(
    [property: JsonPropertyName("claim_id")] string ClaimId,
    [property: JsonPropertyName("claim_code")] string ClaimCode,
    [property: JsonPropertyName("approval_url")] string ApprovalUrl,
    [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt);

public sealed record DeviceClaimStatusResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("device_id")] string? DeviceId,
    [property: JsonPropertyName("camera_id")] string? CameraId,
    [property: JsonPropertyName("device_token")] string? DeviceToken,
    [property: JsonPropertyName("nas_relative_path")] string? NasRelativePath);

public sealed record ProvisioningResultRequest(
    [property: JsonPropertyName("registration_state")] string RegistrationState,
    [property: JsonPropertyName("storage_state")] string StorageState,
    [property: JsonPropertyName("error_code")] string? ErrorCode);

public enum DeviceRegistrationStage
{
    CreatingClaim,
    WaitingForApproval,
    Approved,
    ProvisioningStorage,
    Completed,
    Rejected,
    Expired,
    StorageError,
    ReportError
}

public sealed record DeviceRegistrationProgress(
    DeviceRegistrationStage Stage,
    CreateDeviceClaimResponse? Claim = null,
    string? ErrorCode = null);

public sealed record DeviceRegistrationResult(
    DeviceRegistrationStage Stage,
    string? DeviceId = null,
    string? CameraId = null,
    string? NasRelativePath = null,
    string? ErrorCode = null);
