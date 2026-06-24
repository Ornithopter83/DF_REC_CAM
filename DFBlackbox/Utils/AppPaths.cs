using DFBlackbox.Models;

namespace DFBlackbox.Utils;

public sealed class AppPaths
{
    public string Root { get; }
    public string CameraProfiles { get; }
    public string BaselineReferencePath { get; }
    public string HomeReferencePath { get; }
    public string Logs { get; }
    public string EventLogPath { get; }
    public string EventVideos { get; }
    public string ManualVideos { get; }
    public string TempVideos { get; }
    public string RecVideos { get; }

    public AppPaths(StorageSettings storage)
    {
        Root = storage.RootFolder;
        CameraProfiles = Path.Combine(Root, "camera_profiles");
        BaselineReferencePath = Path.Combine(AppContext.BaseDirectory, "baseline_reference.png");
        HomeReferencePath = Path.Combine(Root, "home_reference.png");
        Logs = Path.Combine(AppContext.BaseDirectory, "Logs");
        EventLogPath = Path.Combine(Logs, "events.jsonl");
        RecVideos = Path.Combine(AppContext.BaseDirectory, "REC");
        EventVideos = Path.Combine(RecVideos, "events");
        ManualVideos = Path.Combine(RecVideos, "manual");
        TempVideos = Path.Combine(RecVideos, "temp");
    }

    public void Ensure()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(CameraProfiles);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(EventVideos);
        Directory.CreateDirectory(ManualVideos);
        Directory.CreateDirectory(TempVideos);
        Directory.CreateDirectory(RecVideos);
    }
}
