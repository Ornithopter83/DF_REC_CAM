namespace DFBlackbox.Models;

public sealed class EventLog
{
    public long Id { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string FilePath { get; set; } = "";
    public string TriggerReason { get; set; } = "";
    public double MaxMotionScore { get; set; }
    public double MaxRodMotionScore { get; set; }
    public double MinHomeDiffScore { get; set; }
    public bool ManualRecording { get; set; }
    public string Memo { get; set; } = "";
}
