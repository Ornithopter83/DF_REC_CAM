namespace DFBlackbox.Models;

public sealed record WindowsDeviceRegistrationInfo(
    string RegistrationName,
    string DeviceId,
    string CameraId,
    string NasRelativePath,
    DateTimeOffset RegisteredAt);
