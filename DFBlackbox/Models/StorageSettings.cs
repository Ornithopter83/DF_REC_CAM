namespace DFBlackbox.Models;

public sealed class StorageSettings
{
    public string RootFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "DFBlackboxData");
    public int MaxStorageGB { get; set; } = 50;
    public int MinFreeDiskGB { get; set; } = 10;
    public int DiskStopThresholdPercent { get; set; } = 90;
    public int DiskResumeThresholdPercent { get; set; } = 85;
    public int RecRetentionDays { get; set; } = 14;
    public int CleanupHour { get; set; } = 3;
    public bool CleanupOnStartup { get; set; } = true;
    public bool StartInTray { get; set; } = false;
    public int EventRetentionDays { get; set; } = 14;
    public int ManualRetentionDays { get; set; } = 30;
    public int TempRetentionHours { get; set; } = 24;
}
