using System.Text.Json;
using DFBlackbox.Utils;

namespace DFBlackbox;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        using var mutex = new Mutex(true, "DFBlackbox.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            Localization.SetLanguage(ReadLanguageSetting());
            MessageBox.Show(Localization.IsEnglish ? "DFBlackbox is already running." : "DFBlackbox가 이미 실행 중입니다.", "DFBlackbox", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        bool startInTray = HasArg(args, "tray")
            || ReadStartInTraySetting();
        //bool recordingOnlyMode = HasArg(args, "reconly");
        bool recordingOnlyMode = true;
        Application.Run(new Forms.MainForm(startInTray, recordingOnlyMode));
        GC.KeepAlive(mutex);
    }

    private static bool HasArg(IEnumerable<string> args, string name)
    {
        return args.Any(arg =>
            string.Equals(arg, "--" + name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "/" + name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "-" + name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ReadStartInTraySetting()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "settings.json");
            if (!File.Exists(path))
            {
                return false;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.TryGetProperty("Storage", out var storage)
                && storage.TryGetProperty("StartInTray", out var startInTray)
                && startInTray.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return startInTray.GetBoolean();
            }
        }
        catch
        {
        }

        return false;
    }

    private static string ReadLanguageSetting()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "settings.json");
            if (!File.Exists(path))
            {
                return Localization.Korean;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty("Language", out var language)
                ? Localization.NormalizeLanguage(language.GetString())
                : Localization.Korean;
        }
        catch
        {
            return Localization.Korean;
        }
    }
}
