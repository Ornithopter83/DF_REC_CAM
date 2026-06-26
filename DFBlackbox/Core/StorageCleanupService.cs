using DFBlackbox.Models;
using DFBlackbox.Utils;

namespace DFBlackbox.Core;

public sealed class StorageCleanupService
{
    public StorageCleanupResult Cleanup(StorageSettings settings)
    {
        var paths = new AppPaths(settings);
        paths.Ensure();
        var result = new StorageCleanupResult();
        DeleteOldFiles(paths.TempVideos, TimeSpan.FromHours(settings.TempRetentionHours), includeLocked: true, result);
        DeleteOldFiles(paths.EventVideos, TimeSpan.FromDays(settings.EventRetentionDays), includeLocked: false, result);
        DeleteOldFiles(paths.ManualVideos, TimeSpan.FromDays(settings.ManualRetentionDays), includeLocked: false, result);
        DeleteOldFiles(paths.RecVideos, TimeSpan.FromDays(settings.RecRetentionDays), includeLocked: false, result);
        EnforceStorageLimit(paths, settings, result);
        return result;
    }

    public long GetFolderSizeBytes(string folder) => DiskUtils.GetFolderSizeBytes(folder);

    public long GetFreeDiskBytes(string folder) => DiskUtils.GetFreeDiskBytes(folder);

    private static void DeleteOldFiles(string folder, TimeSpan maxAge, bool includeLocked, StorageCleanupResult result)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        DateTime cutoff = DateTime.Now - maxAge;
        foreach (var file in Directory.EnumerateFiles(folder, "*.mp4", SearchOption.AllDirectories))
        {
            if (!includeLocked && IsLocked(file))
            {
                continue;
            }

            if (File.GetLastWriteTime(file) < cutoff)
            {
                TryDelete(file, result);
            }
        }
    }

    private static void EnforceStorageLimit(AppPaths paths, StorageSettings settings, StorageCleanupResult result)
    {
        long maxBytes = settings.MaxStorageGB * 1024L * 1024L * 1024L;
        long minFreeBytes = settings.MinFreeDiskGB * 1024L * 1024L * 1024L;

        DeleteUntilOk(paths.EventVideos, paths.Root, maxBytes, minFreeBytes, result);
        DeleteUntilOk(paths.ManualVideos, paths.Root, maxBytes, minFreeBytes, result);
        DeleteUntilOk(paths.RecVideos, paths.RecVideos, maxBytes, minFreeBytes, result);
    }

    private static void DeleteUntilOk(string folder, string root, long maxBytes, long minFreeBytes, StorageCleanupResult result)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(folder, "*.mp4", SearchOption.AllDirectories)
                     .Where(file => !IsLocked(file))
                     .OrderBy(File.GetLastWriteTime))
        {
            long total = DiskUtils.GetFolderSizeBytes(root);
            long free = DiskUtils.GetFreeDiskBytes(root);
            if (total <= maxBytes && free >= minFreeBytes)
            {
                return;
            }

            TryDelete(file, result);
        }
    }

    private static bool IsLocked(string path)
    {
        return path.Contains("_locked", StringComparison.OrdinalIgnoreCase)
            || path.Contains($"{Path.DirectorySeparatorChar}locked{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDelete(string path, StorageCleanupResult result)
    {
        try
        {
            long bytes = new FileInfo(path).Length;
            File.Delete(path);
            result.DeletedFiles++;
            result.FreedBytes += bytes;
        }
        catch
        {
            result.FailedFiles++;
        }
    }
}

public sealed class StorageCleanupResult
{
    public int DeletedFiles { get; set; }
    public int FailedFiles { get; set; }
    public long FreedBytes { get; set; }
}
