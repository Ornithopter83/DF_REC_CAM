using System.Text.Json;

namespace DFBlackbox;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        using var mutex = new Mutex(true, "DFBlackbox.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("DFBlackbox is already running.", "DFBlackbox", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        var startInTray = args.Any(arg =>
            string.Equals(arg, "--tray", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "/tray", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "-tray", StringComparison.OrdinalIgnoreCase))
            || ReadStartInTraySetting();
        Application.Run(new Forms.MainForm(startInTray));
        GC.KeepAlive(mutex);
    }

    private static bool ReadStartInTraySetting()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "settings.json");
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
}
