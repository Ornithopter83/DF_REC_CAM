namespace DFBlackbox.Models;

public sealed class DetectionSettings
{
    public int MotionThreshold { get; set; } = 25;
    public int MinMotionArea { get; set; } = 500;
    public double PersonMotionRatioThreshold { get; set; } = 0.05;
    public double RodMotionRatioThreshold { get; set; } = 0.02;
    public double StrongRodMotionRatioThreshold { get; set; } = 0.08;
    public double HomeDiffThreshold { get; set; } = 18.0;
    public double HomeMotionRatioThreshold { get; set; } = 0.01;
    public int RecordingStopWaitSeconds { get; set; } = 10;
    public int CooldownSeconds { get; set; } = 10;
    public int PreBufferSeconds { get; set; } = 15;
    public int PreBufferMaxMemoryMB { get; set; } = 2048;
}
