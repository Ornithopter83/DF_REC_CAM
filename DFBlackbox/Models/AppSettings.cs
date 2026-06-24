namespace DFBlackbox.Models;

public sealed class AppSettings
{
    public CameraSettings Camera { get; set; } = new();
    public DetectionSettings Detection { get; set; } = new();
    public RoiSettings Rois { get; set; } = new();
    public OverlaySettings Overlay { get; set; } = new();
    public StorageSettings Storage { get; set; } = new();
    public RecordingSettings Recording { get; set; } = new();
}
