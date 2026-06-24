namespace DFBlackbox.Models;

public sealed class StateUpdateResult
{
    public bool ShouldStartRecording { get; set; }
    public bool ShouldStopRecording { get; set; }
    public bool ShouldKeepRecording { get; set; }
    public string TriggerReason { get; set; } = "";
    public string RecordingHoldReason { get; set; } = "";
    public BlackboxState NewState { get; set; }
}
