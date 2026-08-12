using DFBlackbox.Models;

namespace DFBlackbox.Core;

public interface IDeviceRegistrationClient
{
    Task<CreateDeviceClaimResponse> CreateClaimAsync(
        CreateDeviceClaimRequest request,
        CancellationToken cancellationToken);

    Task<DeviceClaimStatusResponse> GetClaimStatusAsync(
        string claimId,
        CancellationToken cancellationToken);

    Task ReportProvisioningResultAsync(
        string deviceId,
        string deviceToken,
        ProvisioningResultRequest request,
        CancellationToken cancellationToken);

    Task RevokeDeviceAsync(
        string deviceId,
        string deviceToken,
        CancellationToken cancellationToken);
}
