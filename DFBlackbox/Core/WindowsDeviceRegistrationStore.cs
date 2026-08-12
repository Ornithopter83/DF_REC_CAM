using DFBlackbox.Models;
using Microsoft.Win32;

namespace DFBlackbox.Core;

public sealed class WindowsDeviceRegistrationStore
{
    private const string RegistryPath = @"Software\DFBlackbox\DeviceRegistration";

    public WindowsDeviceRegistrationInfo? Load()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: false);
        if (key is null)
        {
            return null;
        }

        string registrationName = ReadString(key, "RegistrationName");
        string deviceId = ReadString(key, "DeviceId");
        string cameraId = ReadString(key, "CameraId");
        string nasRelativePath = ReadString(key, "NasRelativePath");
        string registeredAtValue = ReadString(key, "RegisteredAtUtc");
        if (string.IsNullOrWhiteSpace(registrationName)
            || string.IsNullOrWhiteSpace(deviceId)
            || string.IsNullOrWhiteSpace(cameraId)
            || string.IsNullOrWhiteSpace(nasRelativePath)
            || !DateTimeOffset.TryParse(registeredAtValue, out DateTimeOffset registeredAt))
        {
            return null;
        }

        return new WindowsDeviceRegistrationInfo(
            registrationName,
            deviceId,
            cameraId,
            nasRelativePath,
            registeredAt);
    }

    public void Save(WindowsDeviceRegistrationInfo registration)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true);
        key.SetValue("RegistrationName", registration.RegistrationName, RegistryValueKind.String);
        key.SetValue("DeviceId", registration.DeviceId, RegistryValueKind.String);
        key.SetValue("CameraId", registration.CameraId, RegistryValueKind.String);
        key.SetValue("NasRelativePath", registration.NasRelativePath, RegistryValueKind.String);
        key.SetValue("RegisteredAtUtc", registration.RegisteredAt.UtcDateTime.ToString("O"), RegistryValueKind.String);
    }

    public void Delete()
    {
        Registry.CurrentUser.DeleteSubKeyTree(RegistryPath, throwOnMissingSubKey: false);
    }

    private static string ReadString(RegistryKey key, string name) =>
        key.GetValue(name) as string ?? "";
}
