namespace DFBlackbox.Models;

public sealed class RoiSettings
{
    public RoiRect PersonWatchRoi { get; set; } = new() { X = 0, Y = 0, Width = 1280, Height = 720 };
    public RoiRect IgnoreRoi { get; set; } = new() { X = 420, Y = 260, Width = 440, Height = 180 };
    public RoiRect RodControlRoi { get; set; } = new() { X = 300, Y = 300, Width = 600, Height = 300 };
    public RoiRect RodHomeRoi { get; set; } = new() { X = 500, Y = 400, Width = 300, Height = 200 };
    public List<RoiRect> IgnoreRois { get; set; } = [];
}
