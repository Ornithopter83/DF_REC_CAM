namespace DFBlackbox.Core;

public interface IDeviceTokenStore
{
    Task SaveOnceAsync(string deviceId, string deviceToken, CancellationToken cancellationToken);
    Task<string?> LoadAsync(string deviceId, CancellationToken cancellationToken);
    Task DeleteAsync(string deviceId, CancellationToken cancellationToken);
}
