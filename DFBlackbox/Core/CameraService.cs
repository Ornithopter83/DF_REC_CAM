using DFBlackbox.Models;
using DFBlackbox.Utils;
using OpenCvSharp;

namespace DFBlackbox.Core;

public sealed class CameraService : IDisposable
{
    private VideoCapture? _capture;

    public bool IsOpened => _capture?.IsOpened() == true;
    public string ConnectionState { get; private set; } = "Disconnected";
    public string LastOpenedSource { get; private set; } = "";

    public static List<int> FindCameraIndexes(int maxIndex = 10)
    {
        List<int> indexes = new List<int>();
        for (int i = 0; i < maxIndex; i++)
        {
            using var capture = new VideoCapture(i);
            if (capture.IsOpened())
            {
                indexes.Add(i);
            }
        }

        return indexes;
    }

    public bool Open(AppSettings settings)
    {
        Close();
        return settings.Camera.IsIpCamera
            ? OpenIp(settings.Camera.IpCamera)
            : OpenUsb(settings.Camera.UsbCamera);
    }

    public bool OpenUsb(UsbCameraSettings cam)
    {
        Close();
        LastOpenedSource = cam.DeviceIndex.ToString();
        _capture = new VideoCapture(cam.DeviceIndex);
        if (!_capture.IsOpened())
        {
            ConnectionState = "Disconnected";
            Close();
            return false;
        }

        _capture.Set(VideoCaptureProperties.FrameWidth, cam.Width);
        _capture.Set(VideoCaptureProperties.FrameHeight, cam.Height);
        _capture.Set(VideoCaptureProperties.Fps, cam.Fps);
        ConnectionState = "Connected";
        return true;
    }

    public bool OpenIp(IpCameraSettings cam)
    {
        Close();
        string rtspUrl = RtspUrlBuilder.Build(cam);
        LastOpenedSource = rtspUrl;
        _capture = new VideoCapture(rtspUrl);
        if (!_capture.IsOpened())
        {
            ConnectionState = "Disconnected";
            Close();
            return false;
        }

        ConnectionState = "Connected";
        return true;
    }

    public void Close()
    {
        _capture?.Release();
        _capture?.Dispose();
        _capture = null;
        if (ConnectionState != "Reconnecting")
        {
            ConnectionState = "Disconnected";
        }
    }

    public void MarkReconnecting()
    {
        ConnectionState = "Reconnecting";
    }

    public bool TryRead(out Mat frame)
    {
        frame = new Mat();
        if (_capture is null || !_capture.IsOpened())
        {
            ConnectionState = "Disconnected";
            return false;
        }

        if (!_capture.Read(frame) || frame.Empty())
        {
            frame.Dispose();
            frame = new Mat();
            return false;
        }

        ConnectionState = "Connected";
        return true;
    }

    public bool SetProperty(VideoCaptureProperties property, double value)
    {
        return _capture?.Set(property, value) == true;
    }

    public double GetProperty(VideoCaptureProperties property)
    {
        return _capture?.Get(property) ?? 0;
    }

    public void Dispose() => Close();
}
