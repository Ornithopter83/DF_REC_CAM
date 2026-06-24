using DFBlackbox.Models;
using DFBlackbox.Utils;
using OpenCvSharp;
using System.Diagnostics;
using System.Reflection;
using System.Text;

namespace DFBlackbox.Core;

public sealed class RecordingService : IDisposable
{
    private sealed record BufferedFrame(Mat Frame, DateTime Timestamp, long SizeBytes);

    private readonly AppSettings _settings;
    private readonly AppPaths _paths;
    private readonly Queue<BufferedFrame> _preBuffer = new();
    private readonly object _sync = new();
    private IRecordingWriter? _writer;
    private OpenCvSharp.Size _writerSize;
    private string _activeRecordingPath = "";
    private string _activeFinalPath = "";
    private string _triggerReason = "";
    private DateTime _startTime;
    private DateTime _nextFrameDue = DateTime.MinValue;
    private int _recordingFps = 1;
    private TimeSpan _frameInterval = TimeSpan.FromSeconds(1);
    private Mat? _lastWrittenFrame;
    private long _preBufferBytes;

    public RecordingService(AppSettings settings, AppPaths paths)
    {
        _settings = settings;
        _paths = paths;
    }

    public bool IsRecording
    {
        get
        {
            lock (_sync)
            {
                return _writer is not null;
            }
        }
    }
    public string ActiveRecordingPath => _activeRecordingPath;

    public void AddToPreBuffer(Mat frame, DateTime timestamp)
    {
        lock (_sync)
        {
            if (_writer is not null)
            {
                return;
            }

            var clone = frame.Clone();
            var sizeBytes = EstimateMatBytes(clone);
            _preBuffer.Enqueue(new BufferedFrame(clone, timestamp, sizeBytes));
            _preBufferBytes += sizeBytes;
            var maxFrames = Math.Max(1, _settings.Detection.PreBufferSeconds * Math.Max(1, _settings.Camera.ActiveFps));
            var maxBytes = Math.Max(1, _settings.Detection.PreBufferMaxMemoryMB) * 1024L * 1024L;
            while (_preBuffer.Count > maxFrames || _preBufferBytes > maxBytes)
            {
                DisposeBufferedFrame(_preBuffer.Dequeue());
            }
        }
    }

    public void StartRecording(DateTime startTime, string triggerReason, Mat? firstFrame = null)
    {
        lock (_sync)
        {
            if (_writer is not null)
            {
                return;
            }

            _triggerReason = triggerReason;
            _startTime = startTime;
            _recordingFps = GetRecordingFps();
            _frameInterval = TimeSpan.FromSeconds(1.0 / _recordingFps);
            _nextFrameDue = _preBuffer.Count > 0 ? _preBuffer.Peek().Timestamp : startTime;
            _lastWrittenFrame?.Dispose();
            _lastWrittenFrame = null;
            var recordingFolder = GetRecordingFolder(startTime);
            Directory.CreateDirectory(recordingFolder);
            var prefix = CreateUniqueRecordingPrefix(startTime);
            _activeRecordingPath = Path.Combine(recordingFolder, $"{prefix}.recording.mp4");
            _activeFinalPath = Path.Combine(recordingFolder, $"{prefix}.mp4");

            var size = GetRecordingSize(firstFrame);
            _writer = RecordingWriterFactory.Start(_activeRecordingPath, _recordingFps, size, GetRecordingBitrateKbps());
            _writerSize = size;
            foreach (var bufferedFrame in _preBuffer)
            {
                WriteFrameForTimestampUnsafe(bufferedFrame.Frame, bufferedFrame.Timestamp);
            }

            ClearPreBufferUnsafe();
        }
    }

    public void WriteFrame(Mat frame, DateTime timestamp)
    {
        lock (_sync)
        {
            if (_writer is null)
            {
                return;
            }

            WriteFrameForTimestampUnsafe(frame, timestamp);
        }
    }

    public string StopRecording(DateTime endTime)
    {
        lock (_sync)
        {
            if (_writer is null)
            {
                return "";
            }

            _writer.Close();
            _writer = null;
            _writerSize = default;
            _nextFrameDue = DateTime.MinValue;
            _lastWrittenFrame?.Dispose();
            _lastWrittenFrame = null;

            if (File.Exists(_activeFinalPath))
            {
                File.Delete(_activeFinalPath);
            }

            File.Move(_activeRecordingPath, _activeFinalPath);
            ClearPreBufferUnsafe();
            return _activeFinalPath;
        }
    }

    public void RecoverCrashedRecordings()
    {
        foreach (var folder in new[] { _paths.EventVideos, _paths.ManualVideos, _paths.RecVideos })
        {
            if (!Directory.Exists(folder))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(folder, "*.recording.mp4", SearchOption.AllDirectories))
            {
                var crashed = Path.ChangeExtension(file, null) + ".crashed.mp4";
                if (File.Exists(crashed))
                {
                    File.Delete(crashed);
                }

                File.Move(file, crashed);
            }
        }
    }

    private void WriteMatUnsafe(Mat frame)
    {
        if (_writer is null)
        {
            return;
        }

        using var output = new Mat();
        if (frame.Width != _writerSize.Width || frame.Height != _writerSize.Height)
        {
            Cv2.Resize(frame, output, _writerSize);
            _writer.Write(output);
        }
        else
        {
            _writer.Write(frame);
        }
    }

    private void WriteFrameForTimestampUnsafe(Mat frame, DateTime timestamp)
    {
        if (_writer is null)
        {
            return;
        }

        if (_nextFrameDue == DateTime.MinValue)
        {
            _nextFrameDue = timestamp;
        }

        if (timestamp < _nextFrameDue)
        {
            return;
        }

        var framesToWrite = 1 + (int)Math.Floor((timestamp - _nextFrameDue).TotalSeconds * _recordingFps);
        var maxCatchUpFrames = Math.Max(1, _recordingFps * 2);
        if (framesToWrite > maxCatchUpFrames)
        {
            framesToWrite = 1;
            _nextFrameDue = timestamp;
        }

        for (var i = 0; i < framesToWrite; i++)
        {
            WriteMatUnsafe(frame);
            _nextFrameDue += _frameInterval;
        }

        _lastWrittenFrame?.Dispose();
        _lastWrittenFrame = frame.Clone();
    }

    private int GetRecordingFps()
    {
        return Math.Clamp(_settings.Camera.ActiveFps, 1, 60);
    }

    private int GetRecordingBitrateKbps()
    {
        return _settings.Recording.VideoBitrateKbps switch
        {
            320 => 320,
            2500 => 2500,
            _ => 800
        };
    }

    private OpenCvSharp.Size GetRecordingSize(Mat? firstFrame)
    {
        if (firstFrame is not null && !firstFrame.Empty())
        {
            return GetConfiguredRecordingSize(firstFrame.Width, firstFrame.Height);
        }

        if (_preBuffer.Count > 0)
        {
            var frame = _preBuffer.Last().Frame;
            return GetConfiguredRecordingSize(frame.Width, frame.Height);
        }

        return new OpenCvSharp.Size(MakeEven(_settings.Camera.ActiveWidth), MakeEven(_settings.Camera.ActiveHeight));
    }

    private OpenCvSharp.Size GetConfiguredRecordingSize(int frameWidth, int frameHeight)
    {
        var targetWidth = Math.Max(2, _settings.Camera.ActiveWidth);
        var targetHeight = Math.Max(2, _settings.Camera.ActiveHeight);
        var frameAspect = frameWidth / (double)Math.Max(1, frameHeight);
        var targetAspect = targetWidth / (double)Math.Max(1, targetHeight);
        if (Math.Abs(frameAspect - targetAspect) / targetAspect < 0.01)
        {
            return new OpenCvSharp.Size(MakeEven(targetWidth), MakeEven(targetHeight));
        }

        if (frameAspect > targetAspect)
        {
            targetHeight = (int)Math.Round(targetWidth / frameAspect);
        }
        else
        {
            targetWidth = (int)Math.Round(targetHeight * frameAspect);
        }

        return new OpenCvSharp.Size(MakeEven(targetWidth), MakeEven(targetHeight));
    }

    private static int MakeEven(int value)
    {
        value = Math.Max(2, value);
        return value % 2 == 0 ? value : value - 1;
    }

    private string CreateUniqueRecordingPrefix(DateTime startTime)
    {
        var recordingFolder = GetRecordingFolder(startTime);
        var basePrefix = $"{startTime:yyyyMMdd_HHmmss}";
        var prefix = basePrefix;
        var index = 1;
        while (File.Exists(Path.Combine(recordingFolder, $"{prefix}.mp4"))
            || File.Exists(Path.Combine(recordingFolder, $"{prefix}.recording.mp4"))
            || File.Exists(Path.Combine(recordingFolder, $"{prefix}.crashed.mp4")))
        {
            prefix = $"{basePrefix}_{index:000}";
            index++;
        }

        return prefix;
    }

    private string GetRecordingFolder(DateTime timestamp)
    {
        return Path.Combine(
            _paths.RecVideos,
            timestamp.ToString("yyyy"),
            timestamp.ToString("MM"),
            timestamp.ToString("dd"));
    }

    private void ClearPreBufferUnsafe()
    {
        while (_preBuffer.Count > 0)
        {
            DisposeBufferedFrame(_preBuffer.Dequeue());
        }
    }

    private void DisposeBufferedFrame(BufferedFrame bufferedFrame)
    {
        _preBufferBytes = Math.Max(0, _preBufferBytes - bufferedFrame.SizeBytes);
        bufferedFrame.Frame.Dispose();
    }

    private static long EstimateMatBytes(Mat frame)
    {
        try
        {
            return checked((long)frame.Total() * frame.ElemSize());
        }
        catch
        {
            return 0;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _writer?.Dispose();
            _writer = null;
            _lastWrittenFrame?.Dispose();
            _lastWrittenFrame = null;
            ClearPreBufferUnsafe();
        }
    }

    private interface IRecordingWriter : IDisposable
    {
        void Write(Mat frame);
        void Close();
    }

    private static class RecordingWriterFactory
    {
        public static IRecordingWriter Start(string outputPath, int fps, OpenCvSharp.Size size, int bitrateKbps)
        {
            var ffmpegPath = FfmpegRecordingWriter.ResolveFfmpegPath();
            return ffmpegPath is not null
                ? FfmpegRecordingWriter.Start(ffmpegPath, outputPath, fps, bitrateKbps)
                : OpenCvRecordingWriter.Start(outputPath, fps, size);
        }
    }

    private sealed class OpenCvRecordingWriter : IRecordingWriter
    {
        private readonly VideoWriter _writer;
        private bool _closed;

        private OpenCvRecordingWriter(VideoWriter writer)
        {
            _writer = writer;
        }

        public static OpenCvRecordingWriter Start(string outputPath, int fps, OpenCvSharp.Size size)
        {
            var writer = new VideoWriter(outputPath, FourCC.MP4V, fps, size);
            if (!writer.IsOpened())
            {
                writer.Dispose();
                throw new InvalidOperationException("FFmpeg를 찾지 못했고 OpenCV 기본 녹화기도 시작하지 못했습니다.");
            }

            return new OpenCvRecordingWriter(writer);
        }

        public void Write(Mat frame)
        {
            if (_closed)
            {
                return;
            }

            _writer.Write(frame);
        }

        public void Close()
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            _writer.Release();
        }

        public void Dispose()
        {
            Close();
            _writer.Dispose();
        }
    }

    private sealed class FfmpegRecordingWriter : IRecordingWriter
    {
        private const string FfmpegResourceName = "DFBlackbox.ffmpeg.exe";
        private readonly Process _process;
        private readonly StringBuilder _errorOutput = new();
        private bool _closed;

        private FfmpegRecordingWriter(Process process)
        {
            _process = process;
            _process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    _errorOutput.AppendLine(e.Data);
                }
            };
            _process.BeginErrorReadLine();
        }

        public static FfmpegRecordingWriter Start(string ffmpegPath, string outputPath, int fps, int bitrateKbps)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = string.Join(" ", new[]
                    {
                        "-hide_banner",
                        "-loglevel error",
                        "-y",
                        "-f image2pipe",
                        $"-framerate {fps}",
                        "-vcodec bmp",
                        "-i pipe:0",
                        "-an",
                        "-c:v libx264",
                        "-pix_fmt yuv420p",
                        $"-b:v {bitrateKbps}k",
                        $"-maxrate {bitrateKbps}k",
                        $"-bufsize {bitrateKbps * 2}k",
                        "-movflags +faststart",
                        Quote(outputPath)
                    }),
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                },
                EnableRaisingEvents = true
            };

            if (!process.Start())
            {
                process.Dispose();
                throw new InvalidOperationException("FFmpeg 녹화 프로세스를 시작하지 못했습니다.");
            }

            return new FfmpegRecordingWriter(process);
        }

        public void Write(Mat frame)
        {
            if (_closed)
            {
                return;
            }

            try
            {
                Cv2.ImEncode(".bmp", frame, out var bytes);
                _process.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
                _process.StandardInput.BaseStream.Flush();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"FFmpeg 녹화 프레임 쓰기에 실패했습니다. {ex.Message}", ex);
            }
        }

        public void Close()
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            try
            {
                _process.StandardInput.Close();
            }
            catch
            {
            }

            if (!_process.WaitForExit(10000))
            {
                try
                {
                    _process.Kill(entireProcessTree: true);
                }
                catch
                {
                }

                throw new InvalidOperationException("FFmpeg 녹화 프로세스가 정상 종료되지 않았습니다.");
            }

            if (_process.ExitCode != 0)
            {
                var detail = _errorOutput.ToString().Trim();
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail)
                    ? $"FFmpeg 녹화 프로세스가 오류 코드 {_process.ExitCode}로 종료되었습니다."
                    : $"FFmpeg 녹화 프로세스가 오류 코드 {_process.ExitCode}로 종료되었습니다.\n{detail}");
            }
        }

        public void Dispose()
        {
            try
            {
                Close();
            }
            catch
            {
                try
                {
                    if (!_process.HasExited)
                    {
                        _process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                }
            }
            finally
            {
                _process.Dispose();
            }
        }

        public static string? ResolveFfmpegPath()
        {
            var local = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
            if (File.Exists(local))
            {
                return local;
            }

            var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var path in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(path.Trim(), "ffmpeg.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return ExtractBundledFfmpeg();
        }

        private static string? ExtractBundledFfmpeg()
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var resource = assembly.GetManifestResourceStream(FfmpegResourceName);
            if (resource is null)
            {
                return null;
            }

            var toolsFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DFBlackbox",
                "tools");
            Directory.CreateDirectory(toolsFolder);

            var outputPath = Path.Combine(toolsFolder, "ffmpeg.exe");
            if (File.Exists(outputPath) && new FileInfo(outputPath).Length == resource.Length)
            {
                return outputPath;
            }

            var tempPath = outputPath + ".tmp";
            using (var file = File.Create(tempPath))
            {
                resource.CopyTo(file);
            }

            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            File.Move(tempPath, outputPath);
            return outputPath;
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
        }
    }
}
