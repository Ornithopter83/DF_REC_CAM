namespace DFBlackbox.Models;

public sealed class CameraSettings
{
    public string CameraType { get; set; } = "IP";
    public UsbCameraSettings UsbCamera { get; set; } = new();
    public IpCameraSettings IpCamera { get; set; } = new();
    public int NoFrameTimeoutSeconds { get; set; } = 5;
    public int ReconnectDelaySeconds { get; set; } = 3;

    public bool IsIpCamera => string.Equals(CameraType, "IP", StringComparison.OrdinalIgnoreCase);
    public int ActiveWidth => IsIpCamera ? IpCamera.Width : UsbCamera.Width;
    public int ActiveHeight => IsIpCamera ? IpCamera.Height : UsbCamera.Height;
    public int ActiveFps => IsIpCamera ? IpCamera.Fps : UsbCamera.Fps;
}

public sealed class UsbCameraSettings
{
    public int DeviceIndex { get; set; }
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public int Fps { get; set; } = 15;
    public double? Exposure { get; set; }
    public double? Gain { get; set; }
    public double? Brightness { get; set; }
    public double? Contrast { get; set; }
    public double? Saturation { get; set; }
    public double? WhiteBalance { get; set; }
    public double? Focus { get; set; }
}

public sealed class IpCameraSettings
{
    public string IpAddress { get; set; } = "192.168.10.100";
    public int RtspPort { get; set; } = 554;
    public int HttpPort { get; set; } = 80;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string StreamPath { get; set; } = "/stream1";
    public bool UseManualRtspUrl { get; set; }
    public string ManualRtspUrl { get; set; } = "";
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 720;
    public int Fps { get; set; } = 15;
}
