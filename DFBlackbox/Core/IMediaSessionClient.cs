using DFBlackbox.Models;

namespace DFBlackbox.Core;

public interface IMediaSessionClient
{
    Task<DeviceStreamCommand> GetDeviceStreamCommandAsync(
        string deviceId,
        string deviceToken,
        CancellationToken cancellationToken);

    Task ReportDeviceStreamStateAsync(
        string deviceId,
        string deviceToken,
        string cameraId,
        DeviceStreamState state,
        string? errorCode,
        CancellationToken cancellationToken);
}
