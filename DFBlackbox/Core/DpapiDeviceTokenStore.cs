using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DFBlackbox.Core;

public sealed class DpapiDeviceTokenStore : IDeviceTokenStore
{
    private const int CryptProtectUiForbidden = 0x1;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DFBlackbox.DeviceRegistration.v1");
    private readonly string _path;

    public DpapiDeviceTokenStore(string path)
    {
        _path = path;
    }

    public async Task SaveOnceAsync(
        string deviceId,
        string deviceToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(deviceToken))
        {
            throw new ArgumentException("Device ID and token are required.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(_path))
        {
            string? existingToken = await LoadAsync(deviceId, cancellationToken);
            if (existingToken is not null)
            {
                return;
            }

            throw new InvalidOperationException("A token for another device is already stored.");
        }

        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(new TokenEnvelope(deviceId, deviceToken));
        byte[] protectedBytes = Protect(plaintext);
        CryptographicOperations.ZeroMemory(plaintext);

        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = _path + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, protectedBytes, cancellationToken);
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(protectedBytes);
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public async Task<string?> LoadAsync(string deviceId, CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        byte[] protectedBytes = await File.ReadAllBytesAsync(_path, cancellationToken);
        byte[] plaintext = Unprotect(protectedBytes);
        CryptographicOperations.ZeroMemory(protectedBytes);
        try
        {
            TokenEnvelope? envelope = JsonSerializer.Deserialize<TokenEnvelope>(plaintext);
            return envelope is not null
                && string.Equals(envelope.DeviceId, deviceId, StringComparison.Ordinal)
                    ? envelope.DeviceToken
                    : null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public async Task DeleteAsync(string deviceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(_path))
        {
            return;
        }

        string? storedToken = await LoadAsync(deviceId, cancellationToken);
        if (storedToken is null)
        {
            throw new InvalidOperationException("The stored token belongs to another device.");
        }

        File.Delete(_path);
    }

    private static byte[] Protect(byte[] plaintext) => Transform(plaintext, protect: true);

    private static byte[] Unprotect(byte[] protectedBytes) => Transform(protectedBytes, protect: false);

    private static byte[] Transform(byte[] input, bool protect)
    {
        DataBlob inputBlob = CreateBlob(input);
        DataBlob entropyBlob = CreateBlob(Entropy);
        DataBlob outputBlob = default;
        try
        {
            bool success = protect
                ? CryptProtectData(
                    ref inputBlob,
                    null,
                    ref entropyBlob,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out outputBlob)
                : CryptUnprotectData(
                    ref inputBlob,
                    IntPtr.Zero,
                    ref entropyBlob,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    CryptProtectUiForbidden,
                    out outputBlob);
            if (!success)
            {
                throw new CryptographicException(new Win32Exception(Marshal.GetLastWin32Error()).Message);
            }

            byte[] output = new byte[outputBlob.Length];
            Marshal.Copy(outputBlob.Data, output, 0, output.Length);
            return output;
        }
        finally
        {
            FreeBlob(ref inputBlob, localFree: false);
            FreeBlob(ref entropyBlob, localFree: false);
            FreeBlob(ref outputBlob, localFree: true);
        }
    }

    private static DataBlob CreateBlob(byte[] bytes)
    {
        IntPtr data = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, data, bytes.Length);
        return new DataBlob { Length = bytes.Length, Data = data };
    }

    private static void FreeBlob(ref DataBlob blob, bool localFree)
    {
        if (blob.Data == IntPtr.Zero)
        {
            return;
        }

        if (localFree)
        {
            LocalFree(blob.Data);
        }
        else
        {
            Marshal.FreeHGlobal(blob.Data);
        }

        blob = default;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Length;
        public IntPtr Data;
    }

    private sealed record TokenEnvelope(string DeviceId, string DeviceToken);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(
        ref DataBlob dataIn,
        string? description,
        ref DataBlob optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(
        ref DataBlob dataIn,
        IntPtr description,
        ref DataBlob optionalEntropy,
        IntPtr reserved,
        IntPtr promptStruct,
        int flags,
        out DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
