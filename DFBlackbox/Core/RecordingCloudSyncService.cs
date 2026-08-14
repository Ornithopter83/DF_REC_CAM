using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using DFBlackbox.Models;

namespace DFBlackbox.Core;

public sealed record RecordingCloudSyncStatus(
    string FileName,
    bool IsReady,
    string? ErrorCode = null);

public sealed class RecordingCloudSyncService : IAsyncDisposable
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan StabilityDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MinimumFileAge = TimeSpan.FromSeconds(5);
    private static readonly JsonSerializerOptions StateJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly AppSettings _settings;
    private readonly IDeviceTokenStore _tokenStore;
    private readonly HttpRecordingMediaClient _client;
    private readonly string _recordingRoot;
    private readonly string _statePath;
    private readonly object _stateSync = new();
    private readonly Channel<bool> _rescanSignal = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        SingleReader = true,
        SingleWriter = false,
        FullMode = BoundedChannelFullMode.DropWrite
    });
    private readonly CancellationTokenSource _cancellation = new();
    private Dictionary<string, RecordingUploadState> _states = new(StringComparer.OrdinalIgnoreCase);
    private Task? _worker;
    private bool _started;

    public RecordingCloudSyncService(
        AppSettings settings,
        IDeviceTokenStore tokenStore,
        HttpRecordingMediaClient client,
        string recordingRoot,
        string statePath)
    {
        _settings = settings;
        _tokenStore = tokenStore;
        _client = client;
        _recordingRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(recordingRoot));
        _statePath = statePath;
        LoadState();
    }

    public event Action<RecordingCloudSyncStatus>? StatusChanged;

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _worker = Task.Run(() => RunAsync(_cancellation.Token));
    }

    public void TriggerRescan()
    {
        if (_started)
        {
            _rescanSignal.Writer.TryWrite(true);
        }
    }

    public bool IsReadyForCleanup(string filePath)
    {
        string relativePath;
        try
        {
            string fullPath = Path.GetFullPath(filePath);
            if (!fullPath.StartsWith(_recordingRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            relativePath = Path.GetRelativePath(_recordingRoot, fullPath).Replace('\\', '/');
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return false;
        }

        DeviceRegistrationSettings registration = _settings.DeviceRegistration;
        string stateKey = $"{registration.DeviceId}/{registration.CameraId}/{relativePath}";
        lock (_stateSync)
        {
            return _states.TryGetValue(stateKey, out RecordingUploadState? state)
                && string.Equals(state.SyncState, "ready", StringComparison.OrdinalIgnoreCase);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_started)
        {
            _client.Dispose();
            _cancellation.Dispose();
            return;
        }

        _cancellation.Cancel();
        _rescanSignal.Writer.TryComplete();
        if (_worker is not null)
        {
            try
            {
                await _worker;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _client.Dispose();
        _cancellation.Dispose();
        _started = false;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ScanOnceAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(new RecordingCloudSyncStatus("-", false, ErrorCode(ex)));
            }

            using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            waitCancellation.CancelAfter(ScanInterval);
            try
            {
                if (await _rescanSignal.Reader.WaitToReadAsync(waitCancellation.Token))
                {
                    while (_rescanSignal.Reader.TryRead(out _))
                    {
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }
        }
    }

    private async Task ScanOnceAsync(CancellationToken cancellationToken)
    {
        DeviceRegistrationSettings registration = _settings.DeviceRegistration;
        if (string.IsNullOrWhiteSpace(registration.DeviceId)
            || string.IsNullOrWhiteSpace(registration.CameraId)
            || string.IsNullOrWhiteSpace(registration.NasRelativePath))
        {
            return;
        }

        string? deviceToken = await _tokenStore.LoadAsync(registration.DeviceId, cancellationToken);
        if (string.IsNullOrWhiteSpace(deviceToken))
        {
            return;
        }

        if (!Directory.Exists(_recordingRoot))
        {
            return;
        }

        IEnumerable<string> paths;
        try
        {
            paths = Directory.EnumerateFiles(_recordingRoot, "*.mp4", SearchOption.AllDirectories)
                .Where(IsCompletedRecording)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (string path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await SyncFileAsync(
                _recordingRoot,
                path,
                registration.DeviceId,
                registration.CameraId,
                deviceToken,
                cancellationToken);
        }
    }

    private async Task SyncFileAsync(
        string recordingsRoot,
        string filePath,
        string deviceId,
        string cameraId,
        string deviceToken,
        CancellationToken cancellationToken)
    {
        string relativePath = Path.GetRelativePath(recordingsRoot, filePath).Replace('\\', '/');
        if (relativePath.StartsWith("../", StringComparison.Ordinal)
            || string.Equals(relativePath, "..", StringComparison.Ordinal))
        {
            return;
        }

        string stateKey = $"{deviceId}/{cameraId}/{relativePath}";

        try
        {
            var before = new FileInfo(filePath);
            if (!before.Exists)
            {
                return;
            }

            long fileLength = before.Length;
            long writeTicks = before.LastWriteTimeUtc.Ticks;
            if (fileLength <= 0 || DateTime.UtcNow - before.LastWriteTimeUtc < MinimumFileAge)
            {
                return;
            }
            RecordingUploadState? existing;
            lock (_stateSync)
            {
                _states.TryGetValue(stateKey, out existing);
            }
            if (existing is not null
                && existing.FileSizeBytes == fileLength
                && existing.LastWriteTimeUtcTicks == writeTicks
                && string.Equals(existing.SyncState, "ready", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await Task.Delay(StabilityDelay, cancellationToken);
            before.Refresh();
            if (!before.Exists || before.Length != fileLength || before.LastWriteTimeUtc.Ticks != writeTicks)
            {
                return;
            }

            string fingerprint;
            string? existingRecordingId = null;
            if (existing is not null
                && existing.FileSizeBytes == fileLength
                && existing.LastWriteTimeUtcTicks == writeTicks
                && existing.SourceFingerprint.Length == 64)
            {
                fingerprint = existing.SourceFingerprint;
                existingRecordingId = existing.RecordingId;
            }
            else
            {
                fingerprint = await ComputeSha256Async(filePath, cancellationToken);
            }

            var pendingState = new RecordingUploadState(
                fileLength,
                writeTicks,
                fingerprint,
                existingRecordingId,
                "pending");
            lock (_stateSync)
            {
                _states[stateKey] = pendingState;
            }
            await SaveStateAsync(cancellationToken);

            DateTimeOffset lastModified = new(before.LastWriteTimeUtc, TimeSpan.Zero);
            DateTimeOffset recordedAt = TryParseRecordedAt(before.Name, lastModified);
            string nasRelativePath = BuildNasRecordingPath(
                _settings.DeviceRegistration.NasRelativePath,
                relativePath);
            CreateNasUploadSessionResponse uploadSession = await _client.CreateNasUploadSessionAsync(
                deviceId,
                deviceToken,
                cameraId,
                cancellationToken);
            string uploadedNasRelativePath = await _client.UploadToNasAsync(
                uploadSession,
                filePath,
                relativePath,
                nasRelativePath,
                fileLength,
                fingerprint,
                lastModified,
                cancellationToken);
            if (!string.Equals(uploadedNasRelativePath, nasRelativePath, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The NAS completed the upload at an unexpected path.");
            }

            RegisterRecordingCatalogResponse catalog = await _client.RegisterCatalogAsync(
                deviceId,
                deviceToken,
                new RegisterRecordingCatalogRequest(
                    cameraId,
                    nasRelativePath,
                    before.Name,
                    fileLength,
                    fingerprint,
                    recordedAt,
                    null,
                    lastModified),
                cancellationToken);
            if (!string.Equals(catalog.CatalogState, "ready", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The recording server did not confirm the NAS catalog entry.");
            }

            await MarkReadyAsync(stateKey, pendingState, catalog.RecordingId, cancellationToken);
            StatusChanged?.Invoke(new RecordingCloudSyncStatus(before.Name, true));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(new RecordingCloudSyncStatus(Path.GetFileName(filePath), false, ErrorCode(ex)));
        }
    }

    private async Task MarkReadyAsync(
        string stateKey,
        RecordingUploadState state,
        string recordingId,
        CancellationToken cancellationToken)
    {
        lock (_stateSync)
        {
            _states[stateKey] = state with
            {
                RecordingId = recordingId,
                SyncState = "ready"
            };
        }
        await SaveStateAsync(cancellationToken);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private void LoadState()
    {
        if (!File.Exists(_statePath))
        {
            return;
        }

        try
        {
            string json = File.ReadAllText(_statePath);
            Dictionary<string, RecordingUploadState>? loaded =
                JsonSerializer.Deserialize<Dictionary<string, RecordingUploadState>>(json, StateJsonOptions);
            if (loaded is not null)
            {
                lock (_stateSync)
                {
                    _states = new Dictionary<string, RecordingUploadState>(loaded, StringComparer.OrdinalIgnoreCase);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            lock (_stateSync)
            {
                _states = new Dictionary<string, RecordingUploadState>(StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    private async Task SaveStateAsync(CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(_statePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = _statePath + ".tmp";
        try
        {
            string json;
            lock (_stateSync)
            {
                json = JsonSerializer.Serialize(_states, StateJsonOptions);
            }
            await File.WriteAllTextAsync(temporaryPath, json, cancellationToken);
            File.Move(temporaryPath, _statePath, overwrite: true);
        }
        finally
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

    private static bool IsCompletedRecording(string path) =>
        path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
        && !path.EndsWith(".recording.mp4", StringComparison.OrdinalIgnoreCase)
        && !path.EndsWith(".crashed.mp4", StringComparison.OrdinalIgnoreCase)
        && !path.EndsWith(".uploading.mp4", StringComparison.OrdinalIgnoreCase);

    private static string BuildNasRecordingPath(string cameraRelativePath, string recordingRelativePath)
    {
        string cameraPath = cameraRelativePath.Replace('\\', '/').Trim('/');
        string recordingPath = recordingRelativePath.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(cameraPath)
            || string.IsNullOrWhiteSpace(recordingPath)
            || cameraPath.Split('/').Any(segment => segment is "" or "." or "..")
            || recordingPath.Split('/').Any(segment => segment is "" or "." or "..")
            || cameraPath.Contains(':')
            || recordingPath.Contains(':'))
        {
            throw new InvalidDataException("The NAS recording path is invalid.");
        }

        return $"{cameraPath}/recordings/{recordingPath}";
    }

    private static DateTimeOffset TryParseRecordedAt(
        string fileName,
        DateTimeOffset fallback)
    {
        string stem = Path.GetFileNameWithoutExtension(fileName);
        if (stem.Length >= 15
            && DateTime.TryParseExact(
                stem[..15],
                "yyyyMMdd_HHmmss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeLocal,
                out DateTime local))
        {
            return new DateTimeOffset(local);
        }

        return fallback;
    }

    private static string ErrorCode(Exception exception) => exception switch
    {
        HttpRequestException http when http.StatusCode.HasValue => $"http_{(int)http.StatusCode.Value}",
        HttpRequestException => "network_error",
        UnauthorizedAccessException => "nas_access_denied",
        IOException => "io_error",
        CryptographicException => "hash_error",
        _ => "sync_error"
    };

    private sealed record RecordingUploadState(
        long FileSizeBytes,
        long LastWriteTimeUtcTicks,
        string SourceFingerprint,
        string? RecordingId,
        string SyncState);
}
