using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using DFBlackbox.Models;
using DFBlackbox.Utils;
using OpenCvSharp;

namespace DFBlackbox.Core;

/// <summary>
/// Encodes camera frames once to H.264/MPEG-TS, then fans the encoded stream out to
/// independent MP4 and RTMPS remuxers. Recording is the lossless/high-priority consumer;
/// a slow or failed network consumer is detached without back-pressuring recording.
/// </summary>
internal sealed class SharedEncodedMediaPipeline : IDisposable
{
    private const int TransportPacketSize = 188;
    private readonly object _sync = new();
    private readonly string _ffmpegPath;
    private readonly int _fps;
    private readonly int _bitrateKbps;
    private readonly Queue<EncodedChunk> _prebuffer = new();
    private readonly HashSet<Task> _streamingCleanupTasks = [];
    private EncoderRuntime? _encoder;
    private EncodedSink? _recordingSink;
    private EncodedSink? _streamingSink;
    private long _prebufferBytes;
    private int _prebufferSeconds;
    private long _prebufferMaxBytes;
    private bool _prebufferEnabled;
    private bool _streamHasReceivedMedia;
    private long _streamingGeneration;
    private bool _disposed;
    private StreamingPipelineStatus _streamingStatus = new(StreamingPipelineState.Idle);

    public SharedEncodedMediaPipeline(string ffmpegPath, int fps, int bitrateKbps)
    {
        _ffmpegPath = ffmpegPath;
        _fps = Math.Clamp(fps, 1, 60);
        _bitrateKbps = Math.Max(64, bitrateKbps);
    }

    public event EventHandler<StreamingPipelineStatus>? StreamingStatusChanged;

    public bool IsEncoderRunning
    {
        get
        {
            lock (_sync)
            {
                return _encoder is not null;
            }
        }
    }

    public bool IsStreaming
    {
        get
        {
            lock (_sync)
            {
                return _streamingSink is not null;
            }
        }
    }

    public StreamingPipelineStatus StreamingStatus
    {
        get
        {
            EncodedSink? failedStreaming = null;
            StreamingPipelineStatus status;
            lock (_sync)
            {
                if (_streamingSink is not null && !_streamingSink.IsHealthy)
                {
                    failedStreaming = _streamingSink;
                    _streamingSink = null;
                    _streamHasReceivedMedia = false;
                    SetStreamingStatusUnsafe(CreateStreamingStatusUnsafe(
                        StreamingPipelineState.Error,
                        "rtmps_publish_failed"));
                }

                status = _streamingStatus;
            }

            if (failedStreaming is not null)
            {
                ScheduleStreamingSinkCleanup(failedStreaming);
            }

            return status;
        }
    }

    public double CurrentWriterFps
    {
        get
        {
            lock (_sync)
            {
                return _encoder?.CurrentFps ?? 0;
            }
        }
    }

    public void ConfigurePrebuffer(bool enabled, int seconds, int maxMemoryMb)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            _prebufferEnabled = enabled;
            _prebufferSeconds = Math.Max(0, seconds);
            _prebufferMaxBytes = Math.Max(1, maxMemoryMb) * 1024L * 1024L;
            if (!enabled)
            {
                ClearPrebufferUnsafe();
            }
            else
            {
                TrimPrebufferUnsafe(DateTimeOffset.UtcNow);
            }
        }
    }

    public void EnsureEncoder(OpenCvSharp.Size size)
    {
        EncoderRuntime? failed = null;
        try
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                if (_encoder is not null
                    && _encoder.Failure is not null
                    && _recordingSink is null)
                {
                    failed = _encoder;
                    _encoder = null;
                    ClearPrebufferUnsafe();
                }

                if (_encoder is not null)
                {
                    if (_encoder.Size != size)
                    {
                        throw new InvalidOperationException("공유 인코더가 실행 중인 동안 영상 크기를 변경할 수 없습니다.");
                    }

                    return;
                }

                _encoder = EncoderRuntime.Start(
                    _ffmpegPath,
                    size,
                    _fps,
                    _bitrateKbps,
                    OnEncodedChunk,
                    OnEncoderFailed);
            }
        }
        finally
        {
            failed?.Dispose();
        }
    }

    public void WriteFrame(Mat frame, bool recordingPriority)
    {
        EncoderRuntime? encoder;
        EncodedSink? recordingSink;
        lock (_sync)
        {
            ThrowIfDisposed();
            encoder = _encoder;
            recordingSink = _recordingSink;
        }

        if (encoder is null)
        {
            throw new InvalidOperationException("공유 인코더가 시작되지 않았습니다.");
        }

        Exception? failure = encoder.Failure;
        if (failure is not null)
        {
            if (recordingSink is not null)
            {
                throw new InvalidOperationException("공유 H.264 인코더가 중단되었습니다.", failure);
            }

            FailStreaming("shared_encoder_failed");
            return;
        }

        if (recordingSink is not null && !recordingSink.IsHealthy)
        {
            throw new InvalidOperationException("공유 인코딩 녹화 패키징이 중단되었습니다.");
        }

        encoder.Enqueue(frame, recordingPriority || recordingSink is not null);
    }

    public void StartRecording(string outputPath)
    {
        EncodedSink sink = EncodedSink.StartRecording(_ffmpegPath, outputPath);
        try
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                if (_encoder is null)
                {
                    throw new InvalidOperationException("녹화 전에 공유 인코더를 시작해야 합니다.");
                }

                if (_recordingSink is not null)
                {
                    throw new InvalidOperationException("녹화 소비자가 이미 실행 중입니다.");
                }

                _recordingSink = sink;
                foreach (EncodedChunk chunk in _prebuffer)
                {
                    sink.EnqueueRequired(chunk.Data);
                }

                ClearPrebufferUnsafe();
            }
        }
        catch
        {
            sink.Dispose();
            throw;
        }
    }

    public void StopRecording()
    {
        EncodedSink? sink;
        lock (_sync)
        {
            sink = _recordingSink;
            _recordingSink = null;
        }

        if (sink is null)
        {
            return;
        }

        try
        {
            sink.Close(throwOnFailure: true);
        }
        finally
        {
            sink.Dispose();
        }
    }

    public Exception? RotateRecording(string nextOutputPath)
    {
        EncodedSink next = EncodedSink.StartRecording(_ffmpegPath, nextOutputPath);
        EncodedSink previous;
        try
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                previous = _recordingSink
                    ?? throw new InvalidOperationException("회전할 녹화 소비자가 없습니다.");
                // Swap under the same lock used by the encoded fan-out. New packets
                // therefore go to exactly one recording sink while the prior MP4 is
                // finalized outside the critical path.
                _recordingSink = next;
            }
        }
        catch
        {
            next.Dispose();
            throw;
        }

        try
        {
            previous.Close(throwOnFailure: true);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
        finally
        {
            previous.Dispose();
        }
    }

    public void StartStreaming(string ingressUrl, string ingressStreamKey)
    {
        string targetUrl = BuildRtmpTarget(ingressUrl, ingressStreamKey);
        EncodedSink? previous;
        lock (_sync)
        {
            ThrowIfDisposed();
            previous = _streamingSink;
            _streamingSink = null;
            _streamHasReceivedMedia = false;
            _streamingGeneration++;
        }

        if (previous is not null)
        {
            ScheduleStreamingSinkCleanup(previous);
            WaitForStreamingCleanupAsync().GetAwaiter().GetResult();
        }

        EncodedSink sink = EncodedSink.StartStreaming(_ffmpegPath, targetUrl);
        try
        {
            lock (_sync)
            {
                ThrowIfDisposed();
                _streamingSink = sink;
                _streamHasReceivedMedia = false;
                SetStreamingStatusUnsafe(CreateStreamingStatusUnsafe(
                    StreamingPipelineState.Starting));
            }
        }
        catch
        {
            CloseStreamingSink(sink);
            throw;
        }
    }

    public Task StopStreamingAsync()
    {
        EncodedSink? sink;
        lock (_sync)
        {
            sink = _streamingSink;
            _streamingSink = null;
            _streamHasReceivedMedia = false;
            _streamingGeneration++;
            SetStreamingStatusUnsafe(CreateStreamingStatusUnsafe(StreamingPipelineState.Idle));
        }

        if (sink is not null)
        {
            return ScheduleStreamingSinkCleanup(sink);
        }

        return WaitForStreamingCleanupAsync();
    }

    public void StopStreaming()
    {
        _ = StopStreamingAsync();
    }

    public void StopEncoderIfIdle()
    {
        EncoderRuntime? encoder = null;
        lock (_sync)
        {
            if (_recordingSink is null && _streamingSink is null && !_prebufferEnabled)
            {
                encoder = _encoder;
                _encoder = null;
                ClearPrebufferUnsafe();
            }
        }

        encoder?.Dispose();
    }

    public void Dispose()
    {
        EncoderRuntime? encoder;
        EncodedSink? recording;
        EncodedSink? streaming;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            encoder = _encoder;
            recording = _recordingSink;
            streaming = _streamingSink;
            _encoder = null;
            _recordingSink = null;
            _streamingSink = null;
            ClearPrebufferUnsafe();
            _streamingStatus = new StreamingPipelineStatus(StreamingPipelineState.Idle);
        }

        if (streaming is not null)
        {
            ScheduleStreamingSinkCleanup(streaming);
        }

        if (recording is not null)
        {
            try
            {
                recording.Close(throwOnFailure: false);
            }
            catch
            {
            }
            finally
            {
                recording.Dispose();
            }
        }

        encoder?.Dispose();
        WaitForStreamingCleanupAsync().GetAwaiter().GetResult();
    }

    private void OnEncodedChunk(byte[] data, DateTimeOffset receivedAt)
    {
        EncodedSink? failedStreaming = null;
        bool notifyPublishing = false;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            if (_prebufferEnabled && _recordingSink is null)
            {
                byte[] buffered = data.ToArray();
                _prebuffer.Enqueue(new EncodedChunk(buffered, receivedAt));
                _prebufferBytes += buffered.Length;
                TrimPrebufferUnsafe(receivedAt);
            }

            try
            {
                _recordingSink?.EnqueueRequired(data);
            }
            catch (Exception ex)
            {
                // Preserve the failure for the recording thread to observe without
                // terminating the encoder stdout reader (streaming stays isolated).
                _recordingSink?.RecordExternalFailure(ex);
            }

            if (_streamingSink is not null)
            {
                if (_streamingSink.TryEnqueue(data))
                {
                    if (!_streamHasReceivedMedia)
                    {
                        _streamHasReceivedMedia = true;
                        notifyPublishing = true;
                    }
                }
                else
                {
                    failedStreaming = _streamingSink;
                    _streamingSink = null;
                    _streamHasReceivedMedia = false;
                    SetStreamingStatusUnsafe(CreateStreamingStatusUnsafe(
                        StreamingPipelineState.Error,
                        "rtmps_publish_failed"));
                }
            }

            if (notifyPublishing)
            {
                SetStreamingStatusUnsafe(CreateStreamingStatusUnsafe(
                    StreamingPipelineState.Publishing));
            }
        }

        if (failedStreaming is not null)
        {
            ScheduleStreamingSinkCleanup(failedStreaming);
        }
    }

    private void OnEncoderFailed(Exception exception)
    {
        EncodedSink? failedStreaming = null;
        lock (_sync)
        {
            if (_disposed || _streamingSink is null)
            {
                return;
            }

            failedStreaming = _streamingSink;
            _streamingSink = null;
            _streamHasReceivedMedia = false;
            SetStreamingStatusUnsafe(CreateStreamingStatusUnsafe(
                StreamingPipelineState.Error,
                "shared_encoder_failed"));
        }

        if (failedStreaming is not null)
        {
            ScheduleStreamingSinkCleanup(failedStreaming);
        }
    }

    private void FailStreaming(string errorCode)
    {
        EncodedSink? failedStreaming;
        lock (_sync)
        {
            failedStreaming = _streamingSink;
            _streamingSink = null;
            _streamHasReceivedMedia = false;
            SetStreamingStatusUnsafe(CreateStreamingStatusUnsafe(
                StreamingPipelineState.Error,
                errorCode));
        }

        if (failedStreaming is not null)
        {
            ScheduleStreamingSinkCleanup(failedStreaming);
        }
    }

    private void TrimPrebufferUnsafe(DateTimeOffset now)
    {
        DateTimeOffset oldest = now - TimeSpan.FromSeconds(_prebufferSeconds);
        while (_prebuffer.Count > 0
               && (_prebuffer.Peek().ReceivedAt < oldest || _prebufferBytes > _prebufferMaxBytes))
        {
            EncodedChunk removed = _prebuffer.Dequeue();
            _prebufferBytes = Math.Max(0, _prebufferBytes - removed.Data.Length);
        }
    }

    private void ClearPrebufferUnsafe()
    {
        _prebuffer.Clear();
        _prebufferBytes = 0;
    }

    private void SetStreamingStatusUnsafe(StreamingPipelineStatus status)
    {
        if (_streamingStatus == status)
        {
            return;
        }

        _streamingStatus = status;
        EventHandler<StreamingPipelineStatus>? handler = StreamingStatusChanged;
        if (handler is not null)
        {
            ThreadPool.QueueUserWorkItem(_ => handler(this, status));
        }
    }

    private StreamingPipelineStatus CreateStreamingStatusUnsafe(
        StreamingPipelineState state,
        string? errorCode = null)
    {
        return new StreamingPipelineStatus(state, errorCode)
        {
            PublisherGeneration = _streamingGeneration
        };
    }

    private static string BuildRtmpTarget(string ingressUrl, string ingressStreamKey)
    {
        if (string.IsNullOrWhiteSpace(ingressUrl)
            || (!ingressUrl.StartsWith("rtmp://", StringComparison.OrdinalIgnoreCase)
                && !ingressUrl.StartsWith("rtmps://", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("LiveKit RTMP(S) ingress URL이 올바르지 않습니다.", nameof(ingressUrl));
        }

        if (string.IsNullOrWhiteSpace(ingressStreamKey)
            || ingressStreamKey.IndexOfAny(['/', '\\', '?', '#']) >= 0)
        {
            throw new ArgumentException("LiveKit ingress stream key가 올바르지 않습니다.", nameof(ingressStreamKey));
        }

        return $"{ingressUrl.Trim().TrimEnd('/')}/{Uri.EscapeDataString(ingressStreamKey.Trim())}";
    }

    private static void CloseStreamingSink(EncodedSink sink)
    {
        try
        {
            sink.Close(throwOnFailure: false);
        }
        catch
        {
        }
        finally
        {
            sink.Dispose();
        }
    }

    private Task ScheduleStreamingSinkCleanup(EncodedSink sink)
    {
        Task cleanup = Task.Run(() => CloseStreamingSink(sink));
        lock (_sync)
        {
            _streamingCleanupTasks.Add(cleanup);
        }

        _ = cleanup.ContinueWith(
            completedTask =>
            {
                lock (_sync)
                {
                    _streamingCleanupTasks.Remove(completedTask);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return cleanup;
    }

    public async Task WaitForStreamingCleanupAsync()
    {
        while (true)
        {
            Task[] cleanupTasks;
            lock (_sync)
            {
                cleanupTasks = [.. _streamingCleanupTasks];
            }

            if (cleanupTasks.Length == 0)
            {
                return;
            }

            await Task.WhenAll(cleanupTasks).ConfigureAwait(false);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed record EncodedChunk(byte[] Data, DateTimeOffset ReceivedAt);

    private sealed class EncoderRuntime : IDisposable
    {
        private readonly Process _process;
        private readonly BlockingCollection<Mat> _frames;
        private readonly Task _frameWriter;
        private readonly Task _encodedReader;
        private readonly Action<byte[], DateTimeOffset> _chunkHandler;
        private readonly Action<Exception> _failureHandler;
        private readonly FpsCounter _fpsCounter = new();
        private readonly object _failureSync = new();
        private Exception? _failure;
        private bool _stopping;
        private bool _disposed;

        private EncoderRuntime(
            Process process,
            OpenCvSharp.Size size,
            int fps,
            Action<byte[], DateTimeOffset> chunkHandler,
            Action<Exception> failureHandler)
        {
            _process = process;
            Size = size;
            _frames = new BlockingCollection<Mat>(Math.Clamp(fps / 2, 4, 15));
            _chunkHandler = chunkHandler;
            _failureHandler = failureHandler;
            _frameWriter = Task.Run(WriteFrames);
            _encodedReader = Task.Run(ReadEncodedStreamAsync);
        }

        public OpenCvSharp.Size Size { get; }
        public double CurrentFps => _fpsCounter.CurrentFps;

        public Exception? Failure
        {
            get
            {
                lock (_failureSync)
                {
                    return _failure;
                }
            }
        }

        public static EncoderRuntime Start(
            string ffmpegPath,
            OpenCvSharp.Size size,
            int fps,
            int bitrateKbps,
            Action<byte[], DateTimeOffset> chunkHandler,
            Action<Exception> failureHandler)
        {
            ProcessStartInfo startInfo = CreateStartInfo(ffmpegPath);
            startInfo.RedirectStandardOutput = true;
            AddArguments(startInfo,
                "-hide_banner", "-loglevel", "error",
                "-f", "rawvideo",
                "-pixel_format", "bgr24",
                "-video_size", $"{size.Width}x{size.Height}",
                "-framerate", fps.ToString(),
                "-i", "pipe:0",
                "-an",
                "-c:v", "libx264",
                "-preset", "veryfast",
                "-tune", "zerolatency",
                "-pix_fmt", "yuv420p",
                "-b:v", $"{bitrateKbps}k",
                "-maxrate", $"{bitrateKbps}k",
                "-bufsize", $"{bitrateKbps * 2}k",
                "-g", fps.ToString(),
                "-keyint_min", fps.ToString(),
                "-sc_threshold", "0",
                "-x264-params", "repeat-headers=1",
                "-f", "mpegts",
                "-mpegts_flags", "+resend_headers",
                "-muxdelay", "0",
                "-muxpreload", "0",
                "pipe:1");
            Process process = new() { StartInfo = startInfo };
            if (!process.Start())
            {
                process.Dispose();
                throw new InvalidOperationException("공유 H.264 인코더를 시작하지 못했습니다.");
            }

            return new EncoderRuntime(process, size, fps, chunkHandler, failureHandler);
        }

        public void Enqueue(Mat frame, bool required)
        {
            Exception? failure = Failure;
            if (failure is not null)
            {
                throw new InvalidOperationException("공유 H.264 인코더가 중단되었습니다.", failure);
            }

            Mat queued = frame.Clone();
            try
            {
                if (required)
                {
                    _frames.Add(queued);
                }
                else if (!_frames.TryAdd(queued))
                {
                    queued.Dispose();
                }
            }
            catch
            {
                queued.Dispose();
                failure = Failure;
                if (failure is not null)
                {
                    throw new InvalidOperationException("공유 H.264 인코더가 중단되었습니다.", failure);
                }

                throw;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stopping = true;
            _frames.CompleteAdding();
            try
            {
                Task.WaitAll([_frameWriter, _encodedReader], TimeSpan.FromSeconds(12));
            }
            catch
            {
            }

            if (!_process.HasExited)
            {
                try
                {
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit(3000);
                }
                catch
                {
                }
            }

            while (_frames.TryTake(out Mat? frame))
            {
                frame.Dispose();
            }

            _frames.Dispose();
            _process.Dispose();
        }

        private void WriteFrames()
        {
            try
            {
                foreach (Mat frame in _frames.GetConsumingEnumerable())
                {
                    using (frame)
                    {
                        WriteRawBgrFrame(frame, _process.StandardInput.BaseStream);
                        _fpsCounter.Tick();
                    }
                }
            }
            catch (Exception ex)
            {
                RecordFailure(ex);
            }
            finally
            {
                try
                {
                    _process.StandardInput.Close();
                }
                catch
                {
                }
            }
        }

        private async Task ReadEncodedStreamAsync()
        {
            byte[] input = new byte[TransportPacketSize * 128];
            byte[] pending = new byte[TransportPacketSize * 256];
            int pendingCount = 0;
            try
            {
                Stream output = _process.StandardOutput.BaseStream;
                while (true)
                {
                    int read = await output.ReadAsync(input);
                    if (read == 0)
                    {
                        break;
                    }

                    if (pendingCount + read > pending.Length)
                    {
                        Array.Resize(ref pending, Math.Max(pending.Length * 2, pendingCount + read));
                    }

                    Buffer.BlockCopy(input, 0, pending, pendingCount, read);
                    pendingCount += read;
                    int alignedCount = pendingCount - (pendingCount % TransportPacketSize);
                    if (alignedCount == 0)
                    {
                        continue;
                    }

                    byte[] chunk = new byte[alignedCount];
                    Buffer.BlockCopy(pending, 0, chunk, 0, alignedCount);
                    int remainder = pendingCount - alignedCount;
                    if (remainder > 0)
                    {
                        Buffer.BlockCopy(pending, alignedCount, pending, 0, remainder);
                    }

                    pendingCount = remainder;
                    _chunkHandler(chunk, DateTimeOffset.UtcNow);
                }

                if (!_stopping)
                {
                    _process.WaitForExit(3000);
                    if (_process.HasExited && _process.ExitCode != 0)
                    {
                        RecordFailure(new InvalidOperationException("공유 H.264 인코더가 비정상 종료되었습니다."));
                    }
                }
            }
            catch (Exception ex)
            {
                if (!_stopping)
                {
                    RecordFailure(ex);
                }
            }
        }

        private void RecordFailure(Exception exception)
        {
            bool first;
            lock (_failureSync)
            {
                first = _failure is null;
                _failure ??= exception;
            }

            if (first && !_stopping)
            {
                _failureHandler(exception);
            }
        }
    }

    private sealed class EncodedSink : IDisposable
    {
        private readonly Process _process;
        private readonly BlockingCollection<byte[]> _chunks;
        private readonly Task _writer;
        private readonly bool _recording;
        private readonly object _failureSync = new();
        private Exception? _failure;
        private bool _closing;
        private bool _closed;

        private EncodedSink(Process process, bool recording)
        {
            _process = process;
            _recording = recording;
            _chunks = new BlockingCollection<byte[]>(recording ? 2048 : 128);
            _writer = Task.Run(WriteChunks);
        }

        public static EncodedSink StartRecording(string ffmpegPath, string outputPath)
        {
            ProcessStartInfo startInfo = CreateStartInfo(ffmpegPath);
            AddArguments(startInfo,
                "-hide_banner", "-loglevel", "error", "-y",
                "-fflags", "+genpts+discardcorrupt",
                "-f", "mpegts", "-i", "pipe:0",
                "-map", "0:v:0", "-an", "-c:v", "copy",
                "-avoid_negative_ts", "make_zero",
                "-movflags", "+faststart",
                outputPath);
            return Start(startInfo, recording: true);
        }

        public static EncodedSink StartStreaming(string ffmpegPath, string targetUrl)
        {
            ProcessStartInfo startInfo = CreateStartInfo(ffmpegPath);
            AddArguments(startInfo,
                "-hide_banner", "-loglevel", "error",
                "-fflags", "+genpts+nobuffer+discardcorrupt",
                "-f", "mpegts", "-i", "pipe:0",
                "-map", "0:v:0", "-an", "-c:v", "copy",
                "-flvflags", "no_duration_filesize",
                "-f", "flv", targetUrl);
            return Start(startInfo, recording: false);
        }

        public void EnqueueRequired(byte[] data)
        {
            ThrowIfFailed();
            _chunks.Add(data.ToArray());
        }

        public bool TryEnqueue(byte[] data)
        {
            if (HasFailed())
            {
                return false;
            }

            byte[] copy = data.ToArray();
            try
            {
                if (_chunks.TryAdd(copy))
                {
                    return true;
                }
            }
            catch (InvalidOperationException)
            {
            }

            Array.Clear(copy);
            return false;
        }

        public bool IsHealthy => !HasFailed();

        public void Close(bool throwOnFailure)
        {
            if (_closed)
            {
                if (throwOnFailure)
                {
                    ThrowIfFailed();
                }

                return;
            }

            _closing = true;
            _chunks.CompleteAdding();
            try
            {
                if (!_writer.Wait(TimeSpan.FromSeconds(15)))
                {
                    TryKill();
                    RecordFailure(new TimeoutException("FFmpeg media sink did not stop in time."));
                    if (!_writer.Wait(TimeSpan.FromSeconds(3)))
                    {
                        RecordFailure(new TimeoutException(
                            "FFmpeg media sink writer did not stop after process termination."));
                    }
                }
            }
            finally
            {
                _closed = true;
            }

            if (throwOnFailure)
            {
                ThrowIfFailed();
            }
        }

        public void Dispose()
        {
            try
            {
                Close(throwOnFailure: false);
            }
            catch
            {
            }
            finally
            {
                while (_chunks.TryTake(out _))
                {
                }

                _chunks.Dispose();
                _process.Dispose();
            }
        }

        private static EncodedSink Start(ProcessStartInfo startInfo, bool recording)
        {
            Process process = new() { StartInfo = startInfo };
            if (!process.Start())
            {
                process.Dispose();
                throw new InvalidOperationException(recording
                    ? "FFmpeg MP4 패키징 프로세스를 시작하지 못했습니다."
                    : "FFmpeg RTMPS 송출 프로세스를 시작하지 못했습니다.");
            }

            return new EncodedSink(process, recording);
        }

        private void WriteChunks()
        {
            try
            {
                foreach (byte[] chunk in _chunks.GetConsumingEnumerable())
                {
                    _process.StandardInput.BaseStream.Write(chunk);
                }
            }
            catch (Exception ex)
            {
                RecordFailure(ex);
            }
            finally
            {
                try
                {
                    _process.StandardInput.Close();
                }
                catch
                {
                }

                try
                {
                    if (!_process.WaitForExit(10000))
                    {
                        TryKill();
                        RecordFailure(new TimeoutException("FFmpeg media sink did not exit."));
                    }
                    else if (_process.ExitCode != 0 && (!_closing || _recording))
                    {
                        RecordFailure(new InvalidOperationException(_recording
                            ? "FFmpeg MP4 패키징 프로세스가 비정상 종료되었습니다."
                            : "FFmpeg RTMPS 송출 프로세스가 비정상 종료되었습니다."));
                    }
                }
                catch (Exception ex)
                {
                    if (!_closing || _recording)
                    {
                        RecordFailure(ex);
                    }
                }
            }
        }

        private bool HasFailed()
        {
            lock (_failureSync)
            {
                if (_failure is not null)
                {
                    return true;
                }
            }

            if (!_closing && _process.HasExited)
            {
                RecordFailure(new InvalidOperationException(_recording
                    ? "FFmpeg MP4 패키징 프로세스가 중단되었습니다."
                    : "FFmpeg RTMPS 송출 프로세스가 중단되었습니다."));
                return true;
            }

            return false;
        }

        private void ThrowIfFailed()
        {
            Exception? failure;
            lock (_failureSync)
            {
                failure = _failure;
            }

            if (failure is not null)
            {
                throw new InvalidOperationException(_recording
                    ? "공유 인코딩 녹화 패키징에 실패했습니다."
                    : "RTMPS 송출에 실패했습니다.", failure);
            }

            if (HasFailed())
            {
                ThrowIfFailed();
            }
        }

        public void RecordExternalFailure(Exception exception)
        {
            RecordFailure(exception);
        }

        private void RecordFailure(Exception exception)
        {
            lock (_failureSync)
            {
                _failure ??= exception;
            }
        }

        private void TryKill()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit(3000);
                }
            }
            catch
            {
            }
        }
    }

    private static ProcessStartInfo CreateStartInfo(string ffmpegPath)
    {
        return new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            CreateNoWindow = true
        };
    }

    private static void AddArguments(ProcessStartInfo startInfo, params string[] arguments)
    {
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
    }

    private static void WriteRawBgrFrame(Mat frame, Stream output)
    {
        Mat? converted = null;
        Mat? contiguous = null;
        try
        {
            Mat source = frame;
            if (frame.Channels() == 4)
            {
                converted = new Mat();
                Cv2.CvtColor(frame, converted, ColorConversionCodes.BGRA2BGR);
                source = converted;
            }
            else if (frame.Channels() == 1)
            {
                converted = new Mat();
                Cv2.CvtColor(frame, converted, ColorConversionCodes.GRAY2BGR);
                source = converted;
            }
            else if (frame.Channels() != 3)
            {
                throw new InvalidOperationException($"지원하지 않는 프레임 채널 수입니다: {frame.Channels()}");
            }

            if (!source.IsContinuous())
            {
                contiguous = source.Clone();
                source = contiguous;
            }

            int byteCount = checked((int)(source.Total() * source.ElemSize()));
            byte[] buffer = ArrayPool<byte>.Shared.Rent(byteCount);
            try
            {
                Marshal.Copy(source.Data, buffer, 0, byteCount);
                output.Write(buffer, 0, byteCount);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        finally
        {
            contiguous?.Dispose();
            converted?.Dispose();
        }
    }
}
