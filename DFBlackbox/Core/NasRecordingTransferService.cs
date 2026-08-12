using DFBlackbox.Models;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace DFBlackbox.Core;

public sealed record NasRecordingTransferStatus(
    string FileName,
    bool IsTransferred,
    string? ErrorCode = null);

public sealed class NasRecordingTransferService : IAsyncDisposable
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan StabilityDelay = TimeSpan.FromSeconds(1);
    private readonly string _localRecordingRoot;
    private readonly AppSettings _settings;
    private readonly Channel<string> _queue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });
    private readonly ConcurrentDictionary<string, byte> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _transferTask;
    private Task? _scanTask;
    private bool _started;

    public NasRecordingTransferService(string localRecordingRoot, AppSettings settings)
    {
        _localRecordingRoot = Path.GetFullPath(localRecordingRoot);
        _settings = settings;
    }

    public event Action<NasRecordingTransferStatus>? StatusChanged;

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _transferTask = Task.Run(() => TransferLoopAsync(_cancellation.Token));
        _scanTask = Task.Run(() => ScanLoopAsync(_cancellation.Token));
    }

    public void Enqueue(string filePath)
    {
        if (!_started || !IsCompletedRecording(filePath))
        {
            return;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(filePath);
            if (!File.Exists(fullPath) || !NasRecordingPathMapper.IsWithinRoot(fullPath, _localRecordingRoot))
            {
                return;
            }
        }
        catch (Exception) when (filePath is not null)
        {
            return;
        }

        if (_pending.TryAdd(fullPath, 0) && !_queue.Writer.TryWrite(fullPath))
        {
            _pending.TryRemove(fullPath, out _);
        }
    }

    public void TriggerRescan()
    {
        EnqueueExistingFiles();
    }

    public async ValueTask DisposeAsync()
    {
        if (!_started)
        {
            _cancellation.Dispose();
            return;
        }

        _cancellation.Cancel();
        _queue.Writer.TryComplete();
        await AwaitWorkerAsync(_transferTask);
        await AwaitWorkerAsync(_scanTask);
        _cancellation.Dispose();
        _started = false;
    }

    private async Task TransferLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (string sourcePath in _queue.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    NasRecordingTransferStatus? status = await TryTransferAsync(sourcePath, cancellationToken);
                    if (status is not null)
                    {
                        StatusChanged?.Invoke(status);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                finally
                {
                    _pending.TryRemove(sourcePath, out _);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task ScanLoopAsync(CancellationToken cancellationToken)
    {
        EnqueueExistingFiles();
        using var timer = new PeriodicTimer(ScanInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                EnqueueExistingFiles();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void EnqueueExistingFiles()
    {
        if (!Directory.Exists(_localRecordingRoot))
        {
            return;
        }

        try
        {
            foreach (string filePath in Directory.EnumerateFiles(
                _localRecordingRoot,
                "*.mp4",
                SearchOption.AllDirectories))
            {
                Enqueue(filePath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private async Task<NasRecordingTransferStatus?> TryTransferAsync(
        string sourcePath,
        CancellationToken cancellationToken)
    {
        DeviceRegistrationSettings registration = _settings.DeviceRegistration;
        if (!NasRecordingPathMapper.TryGetTargetPath(
                _localRecordingRoot,
                sourcePath,
                registration,
                out string? targetPath,
                out string? errorCode)
            || targetPath is null)
        {
            return errorCode is null
                ? null
                : Failed(sourcePath, errorCode);
        }

        FileInfo before = new(sourcePath);
        long sourceLength = before.Length;
        DateTime sourceWriteTimeUtc = before.LastWriteTimeUtc;
        await Task.Delay(StabilityDelay, cancellationToken);
        before.Refresh();
        if (!before.Exists || before.Length != sourceLength || before.LastWriteTimeUtc != sourceWriteTimeUtc)
        {
            return Failed(sourcePath, "source_not_stable");
        }

        string targetDirectory = Path.GetDirectoryName(targetPath)!;
        Directory.CreateDirectory(targetDirectory);
        if (File.Exists(targetPath))
        {
            return new FileInfo(targetPath).Length == sourceLength
                ? Completed(sourcePath)
                : Failed(sourcePath, "nas_file_conflict");
        }

        string temporaryPath = targetPath + $".{Guid.NewGuid():N}.uploading";
        try
        {
            await using (var source = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var target = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await source.CopyToAsync(target, 1024 * 1024, cancellationToken);
                await target.FlushAsync(cancellationToken);
            }

            if (new FileInfo(temporaryPath).Length != sourceLength)
            {
                return Failed(sourcePath, "nas_length_mismatch");
            }

            File.SetLastWriteTimeUtc(temporaryPath, sourceWriteTimeUtc);
            File.Move(temporaryPath, targetPath);
            temporaryPath = "";
            return Completed(sourcePath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            return Failed(sourcePath, "nas_access_denied");
        }
        catch (IOException)
        {
            return Failed(sourcePath, "nas_io_error");
        }
        finally
        {
            if (!string.IsNullOrEmpty(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch
                {
                }
            }
        }
    }

    private static bool IsCompletedRecording(string? filePath) =>
        !string.IsNullOrWhiteSpace(filePath)
        && filePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
        && !filePath.EndsWith(".recording.mp4", StringComparison.OrdinalIgnoreCase)
        && !filePath.EndsWith(".crashed.mp4", StringComparison.OrdinalIgnoreCase);

    private static NasRecordingTransferStatus Completed(string sourcePath) =>
        new(Path.GetFileName(sourcePath), true);

    private static NasRecordingTransferStatus Failed(string sourcePath, string errorCode) =>
        new(Path.GetFileName(sourcePath), false, errorCode);

    private static async Task AwaitWorkerAsync(Task? task)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }
}

internal static class NasRecordingPathMapper
{
    public static bool TryGetTargetPath(
        string localRecordingRoot,
        string sourcePath,
        DeviceRegistrationSettings registration,
        out string? targetPath,
        out string? errorCode)
    {
        targetPath = null;
        errorCode = null;
        if (string.IsNullOrWhiteSpace(registration.DeviceId)
            || string.IsNullOrWhiteSpace(registration.CameraId)
            || string.IsNullOrWhiteSpace(registration.NasRootFolder)
            || string.IsNullOrWhiteSpace(registration.NasRelativePath))
        {
            return false;
        }

        if (!Directory.Exists(registration.NasRootFolder))
        {
            errorCode = "nas_root_unavailable";
            return false;
        }

        try
        {
            string fullLocalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(localRecordingRoot));
            string fullSourcePath = Path.GetFullPath(sourcePath);
            if (!IsWithinRoot(fullSourcePath, fullLocalRoot))
            {
                errorCode = "invalid_local_recording_path";
                return false;
            }

            string relativePath = Path.GetRelativePath(fullLocalRoot, fullSourcePath);
            string nasCameraRoot = NasProvisioningService.ResolveWithinRoot(
                registration.NasRootFolder,
                registration.NasRelativePath);
            string nasRecordingsRoot = Path.Combine(nasCameraRoot, "recordings");
            string candidate = Path.GetFullPath(Path.Combine(nasRecordingsRoot, relativePath));
            if (!IsWithinRoot(candidate, nasRecordingsRoot))
            {
                errorCode = "invalid_nas_recording_path";
                return false;
            }

            targetPath = candidate;
            return true;
        }
        catch (ArgumentException)
        {
            errorCode = "invalid_nas_recording_path";
            return false;
        }
        catch (NotSupportedException)
        {
            errorCode = "invalid_nas_recording_path";
            return false;
        }
    }

    public static bool IsWithinRoot(string path, string root)
    {
        string fullPath = Path.GetFullPath(path);
        string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return fullPath.StartsWith(
            fullRoot + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }
}
