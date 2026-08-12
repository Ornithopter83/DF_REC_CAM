using DFBlackbox.Models;

namespace DFBlackbox.Core;

public sealed class SimulatedDeviceRegistrationClient : IDeviceRegistrationClient
{
    private readonly object _sync = new();
    private readonly Dictionary<string, DeviceClaimStatusResponse> _claims = new(StringComparer.Ordinal);

    public ProvisioningResultRequest? LastProvisioningResult { get; private set; }

    public Task<CreateDeviceClaimResponse> CreateClaimAsync(
        CreateDeviceClaimRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string claimId = Guid.NewGuid().ToString("N");
        var response = new CreateDeviceClaimResponse(
            claimId,
            "LOCAL-DEMO",
            $"https://localhost.invalid/register?code=LOCAL-DEMO",
            DateTimeOffset.UtcNow.AddMinutes(5));
        lock (_sync)
        {
            _claims[claimId] = new DeviceClaimStatusResponse("pending", null, null, null, null);
        }

        return Task.FromResult(response);
    }

    public Task<DeviceClaimStatusResponse> GetClaimStatusAsync(
        string claimId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (!_claims.TryGetValue(claimId, out DeviceClaimStatusResponse? response))
            {
                throw new KeyNotFoundException("Unknown simulated claim.");
            }

            return Task.FromResult(response);
        }
    }

    public Task ReportProvisioningResultAsync(
        string deviceId,
        string deviceToken,
        ProvisioningResultRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastProvisioningResult = request;
        return Task.CompletedTask;
    }

    public void SetClaimStatus(string claimId, DeviceClaimStatusResponse response)
    {
        lock (_sync)
        {
            if (!_claims.ContainsKey(claimId))
            {
                throw new KeyNotFoundException("Unknown simulated claim.");
            }

            _claims[claimId] = response;
        }
    }
}
