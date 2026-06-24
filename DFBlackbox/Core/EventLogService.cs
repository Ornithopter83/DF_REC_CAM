using System.Text.Json;
using DFBlackbox.Models;

namespace DFBlackbox.Core;

public sealed class EventLogService
{
    private readonly string _path;
    private readonly object _lock = new();
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = false };

    public EventLogService(string path)
    {
        _path = path;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    }

    public void Append(EventLog log)
    {
        lock (_lock)
        {
            log.Id = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            File.AppendAllText(_path, JsonSerializer.Serialize(log, _jsonOptions) + Environment.NewLine);
        }
    }

    public List<EventLog> ReadRecent(int count = 100)
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        return File.ReadLines(_path)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line =>
            {
                try { return JsonSerializer.Deserialize<EventLog>(line); }
                catch { return null; }
            })
            .Where(log => log is not null)
            .Cast<EventLog>()
            .OrderByDescending(log => log.StartTime)
            .Take(count)
            .ToList();
    }

    public void Clear()
    {
        lock (_lock)
        {
            if (File.Exists(_path))
            {
                File.WriteAllText(_path, "");
            }
        }
    }
}
