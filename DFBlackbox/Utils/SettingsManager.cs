using System.Text.Json;
using DFBlackbox.Models;

namespace DFBlackbox.Utils;

public sealed class SettingsManager
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string SettingsPath { get; }
    private string? LegacySettingsPath { get; }

    public SettingsManager(string rootFolder, string? legacyRootFolder = null)
    {
        SettingsPath = Path.Combine(rootFolder, "settings.json");
        LegacySettingsPath = string.IsNullOrWhiteSpace(legacyRootFolder)
            ? null
            : Path.Combine(legacyRootFolder, "settings.json");
    }

    public AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            if (!string.IsNullOrWhiteSpace(LegacySettingsPath) && File.Exists(LegacySettingsPath))
            {
                AppSettings legacySettings = ReadSettings(LegacySettingsPath);
                Save(legacySettings);
                return legacySettings;
            }

            AppSettings settings = new AppSettings();
            Save(settings);
            return settings;
        }

        return ReadSettings(SettingsPath);
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, _jsonOptions));
    }

    private AppSettings ReadSettings(string path)
    {
        try
        {
            string json = File.ReadAllText(path);
            AppSettings settings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions) ?? new AppSettings();
            ApplyLegacyRoiNames(json, settings);
            return settings;
        }
        catch
        {
            AppSettings settings = new AppSettings();
            if (string.Equals(path, SettingsPath, StringComparison.OrdinalIgnoreCase))
            {
                Save(settings);
            }

            return settings;
        }
    }

    private static void ApplyLegacyRoiNames(string json, AppSettings settings)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("Rois", out var rois)
            || rois.TryGetProperty("IgnoreRoi", out _)
            || !rois.TryGetProperty("RodAreaRoi", out var legacyRoi))
        {
            return;
        }

        settings.Rois.IgnoreRoi = new RoiRect
        {
            X = legacyRoi.TryGetProperty("X", out var x) ? x.GetInt32() : settings.Rois.IgnoreRoi.X,
            Y = legacyRoi.TryGetProperty("Y", out var y) ? y.GetInt32() : settings.Rois.IgnoreRoi.Y,
            Width = legacyRoi.TryGetProperty("Width", out var width) ? width.GetInt32() : settings.Rois.IgnoreRoi.Width,
            Height = legacyRoi.TryGetProperty("Height", out var height) ? height.GetInt32() : settings.Rois.IgnoreRoi.Height
        };
    }
}
