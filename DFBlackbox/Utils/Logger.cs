using System.Text.RegularExpressions;

namespace DFBlackbox.Utils;

public sealed class Logger
{
    private static readonly Regex SensitiveMediaUriPattern = new(
        "\\b(?:rtsp|rtsps|rtmp|rtmps)://[^\\s\\\"'<>]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex BearerTokenPattern = new(
        @"\bBearer\s+[^\s,;]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex NamedSecretPattern = new(
        @"\b(?:[a-z0-9]+[_-])*(?:authorization|password|stream[_ -]?key|api[_ -]?(?:key|secret)|secret[_ -]?key|device[_ -]?token|access[_ -]?token|participant[_ -]?token)\s*[:=]\s*[^\s,;]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SupabaseSecretPattern = new(
        @"\bsb_secret_[A-Za-z0-9._~-]+\b",
        RegexOptions.CultureInvariant);
    private static readonly Regex JwtPattern = new(
        @"\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\b",
        RegexOptions.CultureInvariant);
    private readonly string _folder;
    private readonly object _lock = new();

    public Logger(string folder)
    {
        _folder = folder;
        Directory.CreateDirectory(folder);
    }

    public void Info(string message) => Write("INFO", message);
    public void Warning(string message) => Write("WARN", message);

    public void Error(Exception ex, string message)
    {
        lock (_lock)
        {
            string path = CreateExceptionLogPath();
            string text = string.Join(
                Environment.NewLine,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [ERROR] {Redact(message)}",
                Redact(ex.ToString()));
            File.WriteAllText(path, text + Environment.NewLine);
            Write("ERROR", $"{message}: {ex.Message} ({Path.GetFileName(path)})");
        }
    }

    private void Write(string level, string message)
    {
        lock (_lock)
        {
            string path = Path.Combine(_folder, $"app_{DateTime.Now:yyyyMMdd}.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {Redact(message)}{Environment.NewLine}");
        }
    }

    internal static string Redact(string value)
    {
        string redacted = SensitiveMediaUriPattern.Replace(value, match =>
        {
            int schemeEnd = match.Value.IndexOf("://", StringComparison.Ordinal);
            return schemeEnd >= 0 ? $"{match.Value[..schemeEnd]}://[redacted]" : "[redacted]";
        });
        redacted = BearerTokenPattern.Replace(redacted, "Bearer [redacted]");
        redacted = NamedSecretPattern.Replace(redacted, match =>
        {
            int separator = match.Value.IndexOfAny([':', '=']);
            return separator >= 0 ? $"{match.Value[..(separator + 1)]}[redacted]" : "[redacted]";
        });
        redacted = SupabaseSecretPattern.Replace(redacted, "sb_secret_[redacted]");
        return JwtPattern.Replace(redacted, "[jwt-redacted]");
    }

    private string CreateExceptionLogPath()
    {
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string path = Path.Combine(_folder, $"{stamp}.log");
        int index = 1;
        while (File.Exists(path))
        {
            path = Path.Combine(_folder, $"{stamp}_{index:00}.log");
            index++;
        }

        return path;
    }
}
