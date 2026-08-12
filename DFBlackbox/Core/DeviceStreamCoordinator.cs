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
    private readonly object _sync = new();
    private readonly IMediaSessionClient _client;
    private readonly IDeviceTokenStore _tokenStore;
    private readonly DeviceRegistrationSettings _settings;
    private readonly RecordingService _recordingService;
    private readonly Logger _logger;
    private readonly Func<Mat?> _cloneLatestFrame;
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _loop;
    private string? _activeCameraId;
    private string? _activeIngressUrl;
    private string? _activeIngressStreamKey;
    private DateTimeOffset? _activeLeaseUntil;
    private DeviceStreamState? _lastReportedState;
    private string? _lastReportedErrorCode;
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
        _recordingService.StopLiveStreaming();
        _shutdown.Dispose();
        if (_client is IDisposable disposableClient)
        {
            disposableClient.Dispose();
        }
    }

    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TimeSpan delay = PollInterval;
            try
            {
                StopPublisherIfLeaseExpired();
                string deviceId = _settings.DeviceId.Trim();
                string configuredCameraId = _settings.CameraId.Trim();
                if (!Guid.TryParse(deviceId, out _)
                    || !Guid.TryParse(configuredCameraId, out _))
                {
                    await StopPublisherAsync(reportIdle: false, cancellationToken).ConfigureAwait(false);
                    await Task.Delay(FailureDelay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                string? token = await _tokenStore.LoadAsync(deviceId, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(token))
                {
                    await StopPublisherAsync(reportIdle: false, cancellationToken).ConfigureAwait(false);
                    await Task.Delay(FailureDelay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                DeviceStreamCommand command = await _client.GetDeviceStreamCommandAsync(
                    deviceId,
                    token,
                    cancellationToken).ConfigureAwait(false);

                if (!command.ShouldStream)
                {
                    await StopPublisherAsync(
                        reportIdle: true,
                        cancellationToken,
                        token,
                        deviceId,
                        command.CameraId).ConfigureAwait(false);
                }
                else if (!string.Equals(command.CameraId, configuredCameraId, StringComparison.OrdinalIgnoreCase))
                {
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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (MediaSessionException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                _logger.Info($"Live stream device polling was rejected ({ex.ErrorCode}).");
                await StopPublisherAsync(reportIdle: false, CancellationToken.None).ConfigureAwait(false);
                delay = FailureDelay;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Live stream command polling failed");
                StopPublisherIfLeaseExpired();
                delay = FailureDelay;
            }

            try
            {
                await Task.Delay(BoundDelayByLease(delay), cancellationToken).ConfigureAwait(false);
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
        StreamingPipelineState currentState = _recordingService.StreamingStatus.State;
        _activeLeaseUntil = command.LeaseUntil;
        if (currentState is StreamingPipelineState.Starting or StreamingPipelineState.Publishing
            && string.Equals(_activeCameraId, command.CameraId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(_activeIngressUrl, command.IngressUrl, StringComparison.Ordinal)
            && string.Equals(_activeIngressStreamKey, command.IngressStreamKey, StringComparison.Ordinal))
        {
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

        if (currentState == StreamingPipelineState.Error)
        {
            _recordingService.StopLiveStreaming();
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
            if (!_recordingService.StartLiveStreaming(
                    command.IngressUrl,
                    command.IngressStreamKey,
                    firstFrame))
            {
                await ReportStateIfChangedAsync(
                    deviceId,
                    deviceToken,
                    command.CameraId,
                    DeviceStreamState.Error,
                    "ffmpeg_unavailable",
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            _activeCameraId = command.CameraId;
            _activeIngressUrl = command.IngressUrl;
            _activeIngressStreamKey = command.IngressStreamKey;
            _activeLeaseUntil = command.LeaseUntil;
            _lastReportedState = null;
            _lastReportedErrorCode = null;
            _logger.Info($"Live stream publisher requested for camera {ShortId(command.CameraId)}.");
        }
        catch (Exception ex)
        {
            _recordingService.StopLiveStreaming();
            _activeCameraId = null;
            _activeIngressUrl = null;
            _activeIngressStreamKey = null;
            _activeLeaseUntil = null;
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
        _recordingService.StopLiveStreaming();
        _activeCameraId = null;
        _activeIngressUrl = null;
        _activeIngressStreamKey = null;
        _activeLeaseUntil = null;
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

        _ = Task.Run(async () =>
        {
            string deviceId = _settings.DeviceId.Trim();
            string? cameraId = _activeCameraId;
            if (!Guid.TryParse(deviceId, out _) || !Guid.TryParse(cameraId, out _))
            {
                return;
            }

            try
            {
                string? token = await _tokenStore.LoadAsync(deviceId, _shutdown.Token).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(token))
                {
                    return;
                }

                DeviceStreamState state = status.State == StreamingPipelineState.Publishing
                    ? DeviceStreamState.Publishing
                    : DeviceStreamState.Error;
                await ReportStateIfChangedAsync(
                    deviceId,
                    token,
                    cameraId,
                    state,
                    status.ErrorCode,
                    _shutdown.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Live stream state reporting failed");
            }
        });
    }

    private async Task ReportStateIfChangedAsync(
        string deviceId,
        string deviceToken,
        string cameraId,
        DeviceStreamState state,
        string? errorCode,
        CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (_lastReportedState == state
                && string.Equals(_lastReportedErrorCode, errorCode, StringComparison.Ordinal))
            {
                return;
            }
        }

        await _client.ReportDeviceStreamStateAsync(
            deviceId,
            deviceToken,
            cameraId,
            state,
            errorCode,
            cancellationToken).ConfigureAwait(false);
        lock (_sync)
        {
            _lastReportedState = state;
            _lastReportedErrorCode = errorCode;
        }
    }

    private static string ShortId(string? id)
    {
        return id is { Length: >= 8 } ? id[..8] : "unknown";
    }

    private TimeSpan BoundDelayByLease(TimeSpan requestedDelay)
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

    private void StopPublisherIfLeaseExpired()
    {
        DateTimeOffset? leaseUntil = _activeLeaseUntil;
        if (leaseUntil is null || leaseUntil.Value > DateTimeOffset.UtcNow)
        {
            return;
        }

        bool wasActive = _activeCameraId is not null
            || _recordingService.StreamingStatus.State != StreamingPipelineState.Idle;
        _recordingService.StopLiveStreaming();
        _activeCameraId = null;
        _activeIngressUrl = null;
        _activeIngressStreamKey = null;
        _activeLeaseUntil = null;
        if (wasActive)
        {
            _logger.Info("Live stream publisher stopped because the viewer lease expired.");
        }
    }
}
