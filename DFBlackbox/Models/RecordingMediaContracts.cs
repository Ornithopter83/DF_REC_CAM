using System.Text.Json.Serialization;

namespace DFBlackbox.Models;

public sealed record RegisterRecordingCatalogRequest(
    [property: JsonPropertyName("camera_id")] string CameraId,
    [property: JsonPropertyName("nas_relative_path")] string NasRelativePath,
    [property: JsonPropertyName("original_file_name")] string OriginalFileName,
    [property: JsonPropertyName("file_size_bytes")] long FileSizeBytes,
    [property: JsonPropertyName("source_fingerprint")] string SourceFingerprint,
    [property: JsonPropertyName("recorded_at")] DateTimeOffset RecordedAt,
    [property: JsonPropertyName("duration_seconds")] double? DurationSeconds,
    [property: JsonPropertyName("source_last_modified_at")] DateTimeOffset? SourceLastModifiedAt);

public sealed record RegisterRecordingCatalogResponse(
    [property: JsonPropertyName("recording_id")] string RecordingId,
    [property: JsonPropertyName("catalog_state")] string CatalogState);
