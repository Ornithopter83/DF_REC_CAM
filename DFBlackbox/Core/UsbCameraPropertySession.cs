using OpenCvSharp;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace DFBlackbox.Core;

public sealed record UsbCameraPropertyCapability(
    VideoCaptureProperties Property,
    bool Supported,
    int Minimum,
    int Maximum,
    int Step,
    int DefaultValue,
    int CurrentValue,
    bool IsAutomatic,
    bool SupportsAutomatic,
    bool SupportsManual);

public sealed class UsbCameraPropertySession : IDisposable
{
    private const int Success = 0;
    private const int FlagAuto = 0x0001;
    private const int FlagManual = 0x0002;
    private static readonly Guid VideoInputDeviceCategory = new("860BB310-5D01-11D0-BD3B-00A0C911CE86");
    private static readonly Guid BaseFilterInterface = new("56A86895-0AD4-11CE-B03A-0020AF0BA770");
    private readonly object _filter;
    private readonly IAMCameraControl? _cameraControl;
    private readonly IAMVideoProcAmp? _videoProcAmp;
    private bool _disposed;

    private UsbCameraPropertySession(object filter)
    {
        _filter = filter;
        _cameraControl = filter as IAMCameraControl;
        _videoProcAmp = filter as IAMVideoProcAmp;
    }

    public static UsbCameraPropertySession? TryOpen(int deviceIndex)
    {
        object? filter = BindVideoDevice(deviceIndex);
        return filter is null ? null : new UsbCameraPropertySession(filter);
    }

    public UsbCameraPropertyCapability GetCapability(VideoCaptureProperties property)
    {
        ThrowIfDisposed();
        if (!TryMapProperty(property, out PropertyGroup group, out int nativeProperty))
        {
            return Unsupported(property);
        }

        int rangeResult;
        int getResult;
        int minimum;
        int maximum;
        int step;
        int defaultValue;
        int capabilityFlags;
        int currentValue;
        int currentFlags;
        if (group == PropertyGroup.Camera && _cameraControl is not null)
        {
            rangeResult = _cameraControl.GetRange(nativeProperty, out minimum, out maximum, out step, out defaultValue, out capabilityFlags);
            getResult = _cameraControl.Get(nativeProperty, out currentValue, out currentFlags);
        }
        else if (group == PropertyGroup.Video && _videoProcAmp is not null)
        {
            rangeResult = _videoProcAmp.GetRange(nativeProperty, out minimum, out maximum, out step, out defaultValue, out capabilityFlags);
            getResult = _videoProcAmp.Get(nativeProperty, out currentValue, out currentFlags);
        }
        else
        {
            return Unsupported(property);
        }

        if (rangeResult != Success || getResult != Success)
        {
            return Unsupported(property);
        }

        return new UsbCameraPropertyCapability(
            property,
            true,
            minimum,
            maximum,
            Math.Max(1, step),
            defaultValue,
            currentValue,
            (currentFlags & FlagAuto) != 0,
            (capabilityFlags & FlagAuto) != 0,
            (capabilityFlags & FlagManual) != 0);
    }

    public bool TrySet(VideoCaptureProperties property, int value, bool automatic)
    {
        ThrowIfDisposed();
        if (!TryMapProperty(property, out PropertyGroup group, out int nativeProperty))
        {
            return false;
        }

        int flags = automatic ? FlagAuto : FlagManual;
        return group switch
        {
            PropertyGroup.Camera when _cameraControl is not null => _cameraControl.Set(nativeProperty, value, flags) == Success,
            PropertyGroup.Video when _videoProcAmp is not null => _videoProcAmp.Set(nativeProperty, value, flags) == Success,
            _ => false
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (Marshal.IsComObject(_filter))
        {
            Marshal.FinalReleaseComObject(_filter);
        }
    }

    private static object? BindVideoDevice(int deviceIndex)
    {
        object? deviceEnumeratorObject = null;
        IEnumMoniker? monikerEnumerator = null;
        try
        {
            Type? enumeratorType = Type.GetTypeFromCLSID(new Guid("62BE5D10-60EB-11D0-BD3B-00A0C911CE86"));
            if (enumeratorType is null)
            {
                return null;
            }

            deviceEnumeratorObject = Activator.CreateInstance(enumeratorType);
            if (deviceEnumeratorObject is not ICreateDevEnum deviceEnumerator
                || CreateVideoDeviceEnumerator(deviceEnumerator, out monikerEnumerator) != Success
                || monikerEnumerator is null)
            {
                return null;
            }

            var monikers = new IMoniker[1];
            int index = 0;
            while (monikerEnumerator.Next(1, monikers, IntPtr.Zero) == Success)
            {
                IMoniker moniker = monikers[0];
                if (index++ == deviceIndex)
                {
                    try
                    {
                        Guid interfaceId = BaseFilterInterface;
                        moniker.BindToObject(null!, null, ref interfaceId, out object filter);
                        return filter;
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(moniker);
                    }
                }

                Marshal.ReleaseComObject(moniker);
            }

            return null;
        }
        finally
        {
            if (monikerEnumerator is not null && Marshal.IsComObject(monikerEnumerator))
            {
                Marshal.ReleaseComObject(monikerEnumerator);
            }

            if (deviceEnumeratorObject is not null && Marshal.IsComObject(deviceEnumeratorObject))
            {
                Marshal.ReleaseComObject(deviceEnumeratorObject);
            }
        }
    }

    private static int CreateVideoDeviceEnumerator(ICreateDevEnum deviceEnumerator, out IEnumMoniker? monikerEnumerator)
    {
        Guid category = VideoInputDeviceCategory;
        return deviceEnumerator.CreateClassEnumerator(ref category, out monikerEnumerator, 0);
    }

    private static bool TryMapProperty(VideoCaptureProperties property, out PropertyGroup group, out int nativeProperty)
    {
        switch (property)
        {
            case VideoCaptureProperties.Exposure:
                group = PropertyGroup.Camera;
                nativeProperty = 4;
                return true;
            case VideoCaptureProperties.Focus:
                group = PropertyGroup.Camera;
                nativeProperty = 6;
                return true;
            case VideoCaptureProperties.Brightness:
                group = PropertyGroup.Video;
                nativeProperty = 0;
                return true;
            case VideoCaptureProperties.Contrast:
                group = PropertyGroup.Video;
                nativeProperty = 1;
                return true;
            case VideoCaptureProperties.Saturation:
                group = PropertyGroup.Video;
                nativeProperty = 3;
                return true;
            case VideoCaptureProperties.WhiteBalanceBlueU:
                group = PropertyGroup.Video;
                nativeProperty = 7;
                return true;
            case VideoCaptureProperties.Gain:
                group = PropertyGroup.Video;
                nativeProperty = 9;
                return true;
            default:
                group = default;
                nativeProperty = -1;
                return false;
        }
    }

    private static UsbCameraPropertyCapability Unsupported(VideoCaptureProperties property) =>
        new(property, false, 0, 0, 1, 0, 0, false, false, false);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private enum PropertyGroup
    {
        Camera,
        Video
    }

    [ComImport]
    [Guid("29840822-5B84-11D0-BD3B-00A0C911CE86")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICreateDevEnum
    {
        [PreserveSig]
        int CreateClassEnumerator([In] ref Guid category, out IEnumMoniker? enumMoniker, int flags);
    }

    [ComImport]
    [Guid("C6E13370-30AC-11D0-A18C-00A0C9118956")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAMCameraControl
    {
        [PreserveSig]
        int GetRange(int property, out int minimum, out int maximum, out int step, out int defaultValue, out int capabilityFlags);

        [PreserveSig]
        int Set(int property, int value, int flags);

        [PreserveSig]
        int Get(int property, out int value, out int flags);
    }

    [ComImport]
    [Guid("C6E13360-30AC-11D0-A18C-00A0C9118956")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAMVideoProcAmp
    {
        [PreserveSig]
        int GetRange(int property, out int minimum, out int maximum, out int step, out int defaultValue, out int capabilityFlags);

        [PreserveSig]
        int Set(int property, int value, int flags);

        [PreserveSig]
        int Get(int property, out int value, out int flags);
    }
}
