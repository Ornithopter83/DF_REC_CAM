namespace DFBlackbox.Models;

public sealed record DeviceStreamCommand(
    string? CameraId,
    string? RoomName,
    bool ShouldStream,
    string? IngressUrl,
    string? IngressStreamKey,
    DateTimeOffset? LeaseUntil);

public enum DeviceStreamState
{
    Idle,
    Publishing,
    Error
}

public enum StreamingPipelineState
{
    Idle,
    Starting,
    Publishing,
    Error
}

public sealed record StreamingPipelineStatus(
    StreamingPipelineState State,
    string? ErrorCode = null)
{
    public long PublisherGeneration { get; init; }
}
