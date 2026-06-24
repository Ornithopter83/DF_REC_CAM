namespace DFBlackbox.Utils;

public static class DiskUtils
{
    public static long GetFolderSizeBytes(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return 0;
        }

        return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}locked{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Sum(path =>
            {
                try { return new FileInfo(path).Length; }
                catch { return 0; }
            });
    }

    public static long GetFreeDiskBytes(string folder)
    {
        Directory.CreateDirectory(folder);
        var root = Path.GetPathRoot(Path.GetFullPath(folder)) ?? folder;
        return new DriveInfo(root).AvailableFreeSpace;
    }

    public static double GetUsedPercent(string folder)
    {
        Directory.CreateDirectory(folder);
        var root = Path.GetPathRoot(Path.GetFullPath(folder)) ?? folder;
        var drive = new DriveInfo(root);
        if (drive.TotalSize <= 0)
        {
            return 0;
        }

        return (drive.TotalSize - drive.AvailableFreeSpace) * 100.0 / drive.TotalSize;
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##}{units[unit]}";
    }
}
