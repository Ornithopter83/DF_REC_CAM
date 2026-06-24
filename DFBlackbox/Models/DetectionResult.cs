namespace DFBlackbox.Models;

public sealed class DetectionResult
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public bool PersonDetected { get; set; }
    public bool RodMotionDetected { get; set; }
    public bool StrongRodMotionDetected { get; set; }
    public bool HomeSimilar { get; set; }
    public bool HomeMotionLow { get; set; }
    public bool HomeStable { get; set; }
    public double PersonMotionScore { get; set; }
    public double RodMotionScore { get; set; }
    public double HomeDiffScore { get; set; }
    public double HomeMotionScore { get; set; }
    public List<Rectangle> MotionBoxes { get; set; } = [];
    public List<Rectangle> PersonCandidateBoxes { get; set; } = [];
    public string DebugText { get; set; } = "";
}
