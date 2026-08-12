using System.Reflection;

namespace DFBlackbox.Core;

internal static class FfmpegTool
{
    private const string ResourceName = "DFBlackbox.ffmpeg.exe";
    private static readonly object Sync = new();

    public static string? ResolvePath()
    {
        string local = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        if (File.Exists(local))
        {
            return local;
        }

        string pathVariable = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (string path in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(path.Trim(), "ffmpeg.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return ExtractBundledFfmpeg();
    }

    private static string? ExtractBundledFfmpeg()
    {
        lock (Sync)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            using Stream? resource = assembly.GetManifestResourceStream(ResourceName);
            if (resource is null)
            {
                return null;
            }

            string toolsFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DFBlackbox",
                "tools");
            Directory.CreateDirectory(toolsFolder);

            string outputPath = Path.Combine(toolsFolder, "ffmpeg.exe");
            if (File.Exists(outputPath) && new FileInfo(outputPath).Length == resource.Length)
            {
                return outputPath;
            }

            string tempPath = outputPath + ".tmp";
            try
            {
                using (FileStream file = File.Create(tempPath))
                {
                    resource.CopyTo(file);
                }

                File.Move(tempPath, outputPath, overwrite: true);
                return outputPath;
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }
    }
}
