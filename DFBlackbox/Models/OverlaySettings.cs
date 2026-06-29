namespace DFBlackbox.Models;

public sealed class OverlaySettings
{
    public bool ShowPersonBox { get; set; }
    public bool ShowMotionMask { get; set; }
    public bool ShowRodRoi { get; set; }
    public bool ShowHomeRoi { get; set; }
    public bool ShowDebugText { get; set; }
    public bool ShowRecordingStatus { get; set; }
    public bool ShowFrameTime { get; set; }
    public bool ShowPlaybackRoiOutlines { get; set; } = true;
    public bool ShowPlaybackDiffMessage { get; set; } = true;
    public bool ShowPlaybackTrackingCandidate { get; set; } = true;
    public string PlaybackOptimizationMode { get; set; } = "Balanced";
}
