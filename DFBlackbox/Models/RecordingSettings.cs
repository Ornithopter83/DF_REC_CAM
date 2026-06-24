namespace DFBlackbox.Models;

public sealed class RecordingSettings
{
    public string Mode { get; set; } = "Manual";
    public int FullIntervalMinutes { get; set; } = 5;
    public bool AutoStartFullRecording { get; set; } = false;
}
