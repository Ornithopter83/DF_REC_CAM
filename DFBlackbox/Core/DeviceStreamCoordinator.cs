using System.Net;
using DFBlackbox.Models;
using DFBlackbox.Utils;
using OpenCvSharp;

namespace DFBlackbox.Core;

/// <summary>
/// Polls the trusted device endpoint and translates a short viewer lease into a local
/// RTMPS publisher lifecycle. It never logs or persists the ingress stream key.
/// </summary>
public sealed class DeviceStreamCoordinator : IAsyncDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan FailureDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaximumPublishRetryDelay = TimeSpan.FromSeconds(30);
    private readonly object _sync = new();
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private readonly SemaphoreSlim _pollWakeSignal = new(0, 1);
    private readonly HashSet<Task> _statusReportTasks = [];
    private readonly IMediaSessionClient _client;
    private readonly IDeviceTokenStore _tokenStore;
    private readonly DeviceRegistrationSettings _settings;
    private readonly RecordingService _recordingService;
    private readonly Logger _logger;
    private readonly Func<Mat?> _cloneLatestFrame;
    private readonly CancellationTokenSource _shutdown = new();
    private CancellationTokenSource? _activePollRequest;
    private Task? _loop;
    private string? _activeDeviceId;
    private string? _activeDeviceToken;
    private string? _activeCameraId;
    private string? _activeIngressUrl;
    private string? _activeIngressStreamKey;
    private DateTimeOffset? _activeLeaseUntil;
    private long _activePipelineGeneration;
    private DeviceStreamState? _lastReportedState;
    private string? _lastReportedErrorCode;
    private string? _retryCameraId;
    private string? _retryIngressUrl;
    private string? _retryIngressStreamKey;
    private DateTimeOffset _nextPublishAttemptAt = DateTimeOffset.MinValue;
    private int _publishFailureCount;
    private int _lastFailurePublisherGeneration = -1;
    private int _publisherGeneration;
    private int _configurationRevision;
    private bool _disposed;

    public DeviceStreamCoordinator(
        IMediaSessionClient client,
        IDeviceTokenStore tokenStore,
        DeviceRegistrationSettings settings,
        RecordingService recordingService,
        Logger logger,
        Func<Mat?> cloneLatestFrame)
    {
        _client = client;
        _tokenStore = tokenStore;
        _settings = settings;
        _recordingService = recordingService;
        _logger = logger;
        _cloneLatestFrame = cloneLatestFrame;
        _recordingService.StreamingStatusChanged += OnStreamingStatusChanged;
    }

    public void Start()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _loop ??= Task.Run(() => PollLoopAsync(_shutdown.Token));
        }
    }

    internal void NotifyRegistrationChanged()
    {
        CancellationTokenSource? activeRequest;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            Interlocked.Increment(ref _configurationRevision);
            activeRequest = _activePollRequest;
        }

        try
        {
            activeRequest?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            if (_pollWakeSignal.CurrentCount == 0)
            {
                _pollWakeSignal.Release();
            }
        }
        catch (SemaphoreFullException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? loop;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _shutdown.Cancel();
            loop = _loop;
            _loop = null;
        }

        if (loop is not null)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _recordingService.StreamingStatusChanged -= OnStreamingStatusChanged;
        await _stateGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopPublisherUnsafe().ConfigureAwait(false);
        }
        finally
        {
            _stateGate.Release();
        }

        Task[] statusReportTasks;
        lock (_sync)
        {
            statusReportTasks = [.. _statusReportTasks];
        }

        if (statusReportTasks.Length > 0)
        {
            try
            {
                await Task.WhenAll(statusReportTasks).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _shutdown.Dispose();
        if (_client is IDisposable disposableClient)
        {
            disposableClient.Dispose();
        }

        _stateGate.Dispose();
        _pollWakeSignal.Dispose();
    }

    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TimeSpan delay = PollInterval;
            int configurationRevision = Volatile.Read(ref _configurationRevision);
            try
            {
                await StopPublisherIfLeaseExpiredSerializedAsync(cancellationToken).ConfigureAwait(false);
                string deviceId = _settings.DeviceId.Trim();
                string configuredCameraId = _settings.CameraId.Trim();
                if (!Guid.TryParse(deviceId, out _)
                    || !Guid.TryParse(configuredCameraId, out _))
                {
                    await StopPublisherSerializedAsync(
                        reportIdle: false,
                        cancellationToken,
                        resetRetry: true).ConfigureAwait(false);
                    await WaitForNextPollAsync(FailureDelay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                using var pollRequest = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                bool requestIsStale;
                lock (_sync)
                {
                    _activePollRequest = pollRequest;
                    requestIsStale = configurationRevision != Volatile.Read(ref _configurationRevision);
                }

                if (requestIsStale)
                {
                    pollRequest.Cancel();
                }

                string? token;
                DeviceStreamCommand command;
                try
                {
                    token = await _tokenStore.LoadAsync(deviceId, pollRequest.Token).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(token))
                    {
                        await StopPublisherSerializedAsync(
                            reportIdle: false,
                            cancellationToken,
                            resetRetry: true).ConfigureAwait(false);
                        await WaitForNextPollAsync(FailureDelay, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    command = await _client.GetDeviceStreamCommandAsync(
                        deviceId,
                        token,
                        pollRequest.Token).ConfigureAwait(false);
                }
                finally
                {
                    lock (_sync)
                    {
                        if (ReferenceEquals(_activePollRequest, pollRequest))
                        {
                            _activePollRequest = null;
                        }
                    }
                }

                var pollContext = new PollContext(
                    deviceId,
                    configuredCameraId,
                    token,
                    configurationRevision);

                await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (!await IsPollContextCurrentAsync(pollContext, cancellationToken).ConfigureAwait(false))
                    {
                        ResetPublishRetryUnsafe();
                        await StopPublisherUnsafe().ConfigureAwait(false);
                        delay = FailureDelay;
                    }
                    else if (!command.ShouldStream)
                    {
                        ResetPublishRetryUnsafe();
                        await StopPublisherAsync(
                            reportIdle: true,
                            cancellationToken,
                            token,
                            deviceId,
                            command.CameraId).ConfigureAwait(false);
                    }
                    else if (!string.Equals(command.CameraId, configuredCameraId, StringComparison.OrdinalIgnoreCase))
                    {
                        ResetPublishRetryUnsafe();
                        await StopPublisherAsync(reportIdle: false, cancellationToken).ConfigureAwait(false);
                        await ReportStateIfChangedAsync(
                            deviceId,
                            token,
                            command.CameraId ?? configuredCameraId,
                            DeviceStreamState.Error,
                            "camera_registration_mismatch",
                            cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await EnsurePublisherAsync(command, deviceId, token, cancellationToken).ConfigureAwait(false);
                    }
                }
                finally
                {
                    _stateGate.Release();
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException) when (
                configurationRevision != Volatile.Read(ref _configurationRevision))
            {
                await StopPublisherSerializedAsync(reportIdle: false, CancellationToken.None).ConfigureAwait(false);
                delay = TimeSpan.Zero;
            }
            catch (MediaSessionException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                _logger.Info($"Live stream device polling was rejected ({ex.ErrorCode}).");
                await StopPublisherSerializedAsync(
                    reportIdle: false,
                    CancellationToken.None,
                    resetRetry: true).ConfigureAwait(false);
                delay = FailureDelay;
            }
            catch (Exception ex) when (IsExpectedServiceInterruption(ex))
            {
                _logger.Warning(
                    $"Live stream command polling deferred ({ServiceInterruptionCode(ex)}).");
                await StopPublisherIfLeaseExpiredSerializedAsync(CancellationToken.None).ConfigureAwait(false);
                delay = FailureDelay;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Live stream command polling failed");
                await StopPublisherIfLeaseExpiredSerializedAsync(CancellationToken.None).ConfigureAwait(false);
                delay = FailureDelay;
            }

            try
            {
                await WaitForNextPollAsync(
                    await BoundDelayByLeaseSerializedAsync(delay, cancellationToken).ConfigureAwait(false),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task EnsurePublisherAsync(
        DeviceStreamCommand command,
        string deviceId,
        string deviceToken,
        CancellationToken cancellationToken)
    {
        StreamingPipelineStatus currentStatus = _recordingService.StreamingStatus;
        StreamingPipelineState currentState = currentStatus.State;
        if (command.LeaseUntil is null || command.LeaseUntil.Value <= DateTimeOffset.UtcNow)
        {
            ResetPublishRetryUnsafe();
            await StopPublisherAsync(reportIdle: false, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (currentState is StreamingPipelineState.Starting or StreamingPipelineState.Publishing
            && string.Equals(_activeCameraId, command.CameraId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(_activeIngressUrl, command.IngressUrl, StringComparison.Ordinal)
            && string.Equals(_activeIngressStreamKey, command.IngressStreamKey, StringComparison.Ordinal))
        {
            _activeDeviceId = deviceId;
            _activeDeviceToken = deviceToken;
            _activeLeaseUntil = command.LeaseUntil;
            if (currentState == StreamingPipelineState.Publishing
                && command.CameraId is not null)
            {
                await ReportStateIfChangedAsync(
                    deviceId,
                    deviceToken,
                    command.CameraId,
                    DeviceStreamState.Publishing,
                    null,
                    cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        if (currentState is StreamingPipelineState.Starting or StreamingPipelineState.Publishing)
        {
            await StopPublisherAsync(reportIdle: false, cancellationToken).ConfigureAwait(false);
        }

        if (currentState == StreamingPipelineState.Error)
        {
            RegisterPublishFailureUnsafe(command, Volatile.Read(ref _publisherGeneration));
            if (command.CameraId is not null)
            {
                await ReportStateIfChangedAsync(
                    deviceId,
                    deviceToken,
                    command.CameraId,
                    DeviceStreamState.Error,
                    currentStatus.ErrorCode,
                    cancellationToken).ConfigureAwait(false);
            }

            await StopPublisherUnsafe().ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(command.CameraId)
            || string.IsNullOrWhiteSpace(command.IngressUrl)
            || string.IsNullOrWhiteSpace(command.IngressStreamKey))
        {
            await ReportStateIfChangedAsync(
                deviceId,
                deviceToken,
                command.CameraId ?? _settings.CameraId,
                DeviceStreamState.Error,
                "invalid_stream_command",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!CanAttemptPublisherUnsafe(command))
        {
            return;
        }

        using Mat? firstFrame = _cloneLatestFrame();
        if (firstFrame is null || firstFrame.Empty())
        {
            await ReportStateIfChangedAsync(
                deviceId,
                deviceToken,
                command.CameraId,
                DeviceStreamState.Error,
                "camera_frame_unavailable",
                cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            SetActivePublisherUnsafe(command, deviceId, deviceToken);
            if (!_recordingService.StartLiveStreaming(
                    command.IngressUrl,
                    command.IngressStreamKey,
                    firstFrame))
            {
                await StopPublisherUnsafe().ConfigureAwait(false);
                RegisterPublishFailureUnsafe(command, Volatile.Read(ref _publisherGeneration));
                await ReportStateIfChangedAsync(
                    deviceId,
                    deviceToken,
                    command.CameraId,
                    DeviceStreamState.Error,
                    "ffmpeg_unavailable",
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            _activePipelineGeneration = _recordingService.StreamingStatus.PublisherGeneration;
            _logger.Info($"Live stream publisher requested for camera {ShortId(command.CameraId)}.");
        }
        catch (Exception ex)
        {
            await StopPublisherUnsafe().ConfigureAwait(false);
            RegisterPublishFailureUnsafe(command, Volatile.Read(ref _publisherGeneration));
            _logger.Error(ex, $"Live stream publisher failed for camera {ShortId(command.CameraId)}");
            await ReportStateIfChangedAsync(
                deviceId,
                deviceToken,
                command.CameraId,
                DeviceStreamState.Error,
                "rtmps_publish_start_failed",
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task StopPublisherAsync(
        bool reportIdle,
        CancellationToken cancellationToken,
        string? deviceToken = null,
        string? deviceId = null,
        string? fallbackCameraId = null)
    {
        string? activeCameraId = _activeCameraId;
        string? cameraId = activeCameraId ?? fallbackCameraId;
        bool wasActive = activeCameraId is not null
            || _recordingService.StreamingStatus.State != StreamingPipelineState.Idle;
        await StopPublisherUnsafe().ConfigureAwait(false);
        if (wasActive)
        {
            _logger.Info("Live stream publisher stopped.");
        }

        if (reportIdle
            && cameraId is not null
            && !string.IsNullOrWhiteSpace(deviceToken)
            && !string.IsNullOrWhiteSpace(deviceId))
        {
            await ReportStateIfChangedAsync(
                deviceId,
                deviceToken,
                cameraId,
                DeviceStreamState.Idle,
                null,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private void OnStreamingStatusChanged(object? sender, StreamingPipelineStatus status)
    {
        if (status.State is not (StreamingPipelineState.Publishing or StreamingPipelineState.Error))
        {
            return;
        }

        Task reportTask;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            int generation = Volatile.Read(ref _publisherGeneration);
            reportTask = ReportStreamingStatusAsync(status, generation);
            _statusReportTasks.Add(reportTask);
        }

        _ = reportTask.ContinueWith(
            completedTask =>
            {
                lock (_sync)
                {
                    _statusReportTasks.Remove(completedTask);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task ReportStreamingStatusAsync(
        StreamingPipelineStatus status,
        int publisherGeneration)
    {
        try
        {
            await _stateGate.WaitAsync(_shutdown.Token).ConfigureAwait(false);
            try
            {
                if (publisherGeneration != Volatile.Read(ref _publisherGeneration))
                {
                    return;
                }

                StreamingPipelineStatus currentStatus = _recordingService.StreamingStatus;
                if (status.PublisherGeneration != _activePipelineGeneration
                    || currentStatus.PublisherGeneration != _activePipelineGeneration
                    || currentStatus.State != status.State
                    || !string.Equals(currentStatus.ErrorCode, status.ErrorCode, StringComparison.Ordinal))
                {
                    return;
                }

                string? deviceId = _activeDeviceId;
                string? deviceToken = _activeDeviceToken;
                string? cameraId = _activeCameraId;
                if (!Guid.TryParse(deviceId, out _)
                    || !Guid.TryParse(cameraId, out _)
                    || string.IsNullOrWhiteSpace(deviceToken)
                    || !string.Equals(_settings.DeviceId.Trim(), deviceId, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(_settings.CameraId.Trim(), cameraId, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                DeviceStreamState state = status.State == StreamingPipelineState.Publishing
                    ? DeviceStreamState.Publishing
                    : DeviceStreamState.Error;
                if (state == DeviceStreamState.Publishing)
                {
                    ResetPublishRetryUnsafe();
                }
                else
                {
                    RegisterPublishFailureUnsafe(
                        new DeviceStreamCommand(
                            cameraId,
                            null,
                            true,
                            _activeIngressUrl,
                            _activeIngressStreamKey,
                            _activeLeaseUntil),
                        publisherGeneration);
                }

                await ReportStateIfChangedAsync(
                    deviceId,
                    deviceToken,
                    cameraId,
                    state,
                    status.ErrorCode,
                    _shutdown.Token).ConfigureAwait(false);
            }
            finally
            {
                _stateGate.Release();
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (IsExpectedServiceInterruption(ex))
        {
            _logger.Warning(
                $"Live stream state reporting deferred ({ServiceInterruptionCode(ex)}).");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Live stream state reporting failed");
        }
    }

    private async Task ReportStateIfChangedAsync(
        string deviceId,
        string deviceToken,
        string cameraId,
        DeviceStreamState state,
        string? errorCode,
        CancellationToken cancellationToken)
    {
        if (_lastReportedState == state
            && string.Equals(_lastReportedErrorCode, errorCode, StringComparison.Ordinal))
        {
            return;
        }

        await _client.ReportDeviceStreamStateAsync(
            deviceId,
            deviceToken,
            cameraId,
            state,
            errorCode,
            cancellationToken).ConfigureAwait(false);
        _lastReportedState = state;
        _lastReportedErrorCode = errorCode;
    }

    private static string ShortId(string? id)
    {
        return id is { Length: >= 8 } ? id[..8] : "unknown";
    }

    private static bool IsExpectedServiceInterruption(Exception exception)
    {
        return exception is TaskCanceledException
            or HttpRequestException
            || exception is MediaSessionException mediaSessionException
                && (int)mediaSessionException.StatusCode >= 500;
    }

    private static string ServiceInterruptionCode(Exception exception)
    {
        return exception switch
        {
            MediaSessionException mediaSessionException =>
                $"service_http_{(int)mediaSessionException.StatusCode}",
            HttpRequestException httpRequestException when httpRequestException.StatusCode.HasValue =>
                $"network_http_{(int)httpRequestException.StatusCode.Value}",
            TaskCanceledException => "network_timeout",
            HttpRequestException => "network_unavailable",
            _ => "service_unavailable"
        };
    }

    private async Task<TimeSpan> BoundDelayByLeaseSerializedAsync(
        TimeSpan requestedDelay,
        CancellationToken cancellationToken)
    {
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return BoundDelayByLeaseUnsafe(requestedDelay);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    private TimeSpan BoundDelayByLeaseUnsafe(TimeSpan requestedDelay)
    {
        DateTimeOffset? leaseUntil = _activeLeaseUntil;
        if (leaseUntil is null)
        {
            return requestedDelay;
        }

        TimeSpan remaining = leaseUntil.Value - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return remaining < requestedDelay ? remaining : requestedDelay;
    }

    private async Task StopPublisherIfLeaseExpiredSerializedAsync(CancellationToken cancellationToken)
    {
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopPublisherIfLeaseExpiredUnsafe().ConfigureAwait(false);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    private async Task StopPublisherIfLeaseExpiredUnsafe()
    {
        DateTimeOffset? leaseUntil = _activeLeaseUntil;
        if (leaseUntil is null || leaseUntil.Value > DateTimeOffset.UtcNow)
        {
            return;
        }

        bool wasActive = _activeCameraId is not null
            || _recordingService.StreamingStatus.State != StreamingPipelineState.Idle;
        await StopPublisherUnsafe().ConfigureAwait(false);
        ResetPublishRetryUnsafe();
        if (wasActive)
        {
            _logger.Info("Live stream publisher stopped because the viewer lease expired.");
        }
    }

    private async Task StopPublisherSerializedAsync(
        bool reportIdle,
        CancellationToken cancellationToken,
        bool resetRetry = false)
    {
        await _stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (resetRetry)
            {
                ResetPublishRetryUnsafe();
            }

            await StopPublisherAsync(reportIdle, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    private async Task<bool> IsPollContextCurrentAsync(
        PollContext context,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(_settings.DeviceId.Trim(), context.DeviceId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(_settings.CameraId.Trim(), context.CameraId, StringComparison.OrdinalIgnoreCase)
            || context.ConfigurationRevision != Volatile.Read(ref _configurationRevision))
        {
            return false;
        }

        string? currentToken = await _tokenStore.LoadAsync(context.DeviceId, cancellationToken).ConfigureAwait(false);
        return string.Equals(currentToken, context.DeviceToken, StringComparison.Ordinal);
    }

    private void SetActivePublisherUnsafe(
        DeviceStreamCommand command,
        string deviceId,
        string deviceToken)
    {
        Interlocked.Increment(ref _publisherGeneration);
        _activeDeviceId = deviceId;
        _activeDeviceToken = deviceToken;
        _activeCameraId = command.CameraId;
        _activeIngressUrl = command.IngressUrl;
        _activeIngressStreamKey = command.IngressStreamKey;
        _activeLeaseUntil = command.LeaseUntil;
        _activePipelineGeneration = 0;
        _lastReportedState = null;
        _lastReportedErrorCode = null;
    }

    private async Task StopPublisherUnsafe()
    {
        Interlocked.Increment(ref _publisherGeneration);
        _recordingService.StopLiveStreaming();
        _activeDeviceId = null;
        _activeDeviceToken = null;
        _activeCameraId = null;
        _activeIngressUrl = null;
        _activeIngressStreamKey = null;
        _activeLeaseUntil = null;
        _activePipelineGeneration = 0;
        await _recordingService.WaitForLiveStreamingStopAsync().ConfigureAwait(false);
    }

    private bool CanAttemptPublisherUnsafe(DeviceStreamCommand command)
    {
        if (!IsRetryTargetUnsafe(command))
        {
            ResetPublishRetryUnsafe();
            return true;
        }

        return DateTimeOffset.UtcNow >= _nextPublishAttemptAt;
    }

    private void RegisterPublishFailureUnsafe(
        DeviceStreamCommand command,
        int publisherGeneration)
    {
        if (publisherGeneration == _lastFailurePublisherGeneration
            && IsRetryTargetUnsafe(command))
        {
            return;
        }

        if (!IsRetryTargetUnsafe(command))
        {
            _publishFailureCount = 0;
        }

        _retryCameraId = command.CameraId;
        _retryIngressUrl = command.IngressUrl;
        _retryIngressStreamKey = command.IngressStreamKey;
        _lastFailurePublisherGeneration = publisherGeneration;
        _publishFailureCount = Math.Min(_publishFailureCount + 1, 16);
        double delaySeconds = Math.Min(
            MaximumPublishRetryDelay.TotalSeconds,
            PollInterval.TotalSeconds * Math.Pow(2, _publishFailureCount - 1));
        _nextPublishAttemptAt = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(delaySeconds);
        _logger.Info(
            $"Live stream publisher retry scheduled in {delaySeconds:0} seconds " +
            $"for camera {ShortId(command.CameraId)}.");
    }

    private bool IsRetryTargetUnsafe(DeviceStreamCommand command)
    {
        return string.Equals(_retryCameraId, command.CameraId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(_retryIngressUrl, command.IngressUrl, StringComparison.Ordinal)
            && string.Equals(_retryIngressStreamKey, command.IngressStreamKey, StringComparison.Ordinal);
    }

    private void ResetPublishRetryUnsafe()
    {
        _retryCameraId = null;
        _retryIngressUrl = null;
        _retryIngressStreamKey = null;
        _nextPublishAttemptAt = DateTimeOffset.MinValue;
        _publishFailureCount = 0;
        _lastFailurePublisherGeneration = -1;
    }

    private async Task WaitForNextPollAsync(
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        await _pollWakeSignal.WaitAsync(delay, cancellationToken).ConfigureAwait(false);
    }

    private sealed record PollContext(
        string DeviceId,
        string CameraId,
        string DeviceToken,
        int ConfigurationRevision);
}
