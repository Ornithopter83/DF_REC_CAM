using DFBlackbox.Models;

namespace DFBlackbox.Utils;

public static class RtspUrlBuilder
{
    public static string Build(IpCameraSettings cam)
    {
        if (cam.UseManualRtspUrl && !string.IsNullOrWhiteSpace(cam.ManualRtspUrl))
        {
            return cam.ManualRtspUrl.Trim();
        }

        var userInfo = "";
        if (!string.IsNullOrWhiteSpace(cam.Username))
        {
            userInfo = string.IsNullOrWhiteSpace(cam.Password)
                ? $"{Uri.EscapeDataString(cam.Username)}@"
                : $"{Uri.EscapeDataString(cam.Username)}:{Uri.EscapeDataString(cam.Password)}@";
        }

        var path = cam.StreamPath?.Trim() ?? "";
        if (!path.StartsWith("/", StringComparison.Ordinal))
        {
            path = "/" + path;
        }

        return $"rtsp://{userInfo}{cam.IpAddress}:{cam.RtspPort}{path}";
    }
}
