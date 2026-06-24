namespace DFBlackbox.Utils;

public sealed class Logger
{
    private readonly string _folder;
    private readonly object _lock = new();

    public Logger(string folder)
    {
        _folder = folder;
        Directory.CreateDirectory(folder);
    }

    public void Info(string message) => Write("INFO", message);

    public void Error(Exception ex, string message)
    {
        lock (_lock)
        {
            var path = CreateExceptionLogPath();
            var text = string.Join(
                Environment.NewLine,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [ERROR] {message}",
                ex.ToString());
            File.WriteAllText(path, text + Environment.NewLine);
            Write("ERROR", $"{message}: {ex.Message} ({Path.GetFileName(path)})");
        }
    }

    private void Write(string level, string message)
    {
        lock (_lock)
        {
            var path = Path.Combine(_folder, $"app_{DateTime.Now:yyyyMMdd}.log");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
        }
    }

    private string CreateExceptionLogPath()
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var path = Path.Combine(_folder, $"{stamp}.log");
        var index = 1;
        while (File.Exists(path))
        {
            path = Path.Combine(_folder, $"{stamp}_{index:00}.log");
            index++;
        }

        return path;
    }
}
