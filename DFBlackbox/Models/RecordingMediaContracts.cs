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

public sealed record CreateNasUploadSessionRequest(
    [property: JsonPropertyName("camera_id")] string CameraId);

public sealed record CreateNasUploadSessionResponse(
    [property: JsonPropertyName("gateway_base_url")] string GatewayBaseUrl,
    [property: JsonPropertyName("upload_url")] string UploadUrl,
    [property: JsonPropertyName("session_expires_at")] DateTimeOffset SessionExpiresAt,
    [property: JsonPropertyName("chunk_size_bytes")] int ChunkSizeBytes,
    [property: JsonPropertyName("assertion")] string Assertion);

public sealed record CreateNasUploadRequest(
    [property: JsonPropertyName("relative_path")] string RelativePath,
    [property: JsonPropertyName("file_size_bytes")] long FileSizeBytes,
    [property: JsonPropertyName("source_fingerprint")] string SourceFingerprint,
    [property: JsonPropertyName("source_last_modified_at")] DateTimeOffset? SourceLastModifiedAt);

public sealed record NasUploadResponse(
    [property: JsonPropertyName("upload_id")] string UploadId,
    [property: JsonPropertyName("offset")] long Offset,
    [property: JsonPropertyName("length")] long Length,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("nas_relative_path")] string? NasRelativePath);
