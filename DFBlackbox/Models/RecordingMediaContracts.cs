using System.Text.Json.Serialization;

namespace DFBlackbox.Models;

public sealed record BeginRecordingUploadRequest(
    [property: JsonPropertyName("camera_id")] string CameraId,
    [property: JsonPropertyName("source_relative_path")] string SourceRelativePath,
    [property: JsonPropertyName("original_file_name")] string OriginalFileName,
    [property: JsonPropertyName("file_size_bytes")] long FileSizeBytes,
    [property: JsonPropertyName("source_fingerprint")] string SourceFingerprint,
    [property: JsonPropertyName("recorded_at")] DateTimeOffset RecordedAt,
    [property: JsonPropertyName("duration_seconds")] double? DurationSeconds,
    [property: JsonPropertyName("source_last_modified_at")] DateTimeOffset? SourceLastModifiedAt);

public sealed record BeginRecordingUploadResponse(
    [property: JsonPropertyName("recording_id")] string RecordingId,
    [property: JsonPropertyName("sync_state")] string SyncState,
    [property: JsonPropertyName("upload_required")] bool UploadRequired,
    [property: JsonPropertyName("bucket")] string? Bucket,
    [property: JsonPropertyName("object_path")] string? ObjectPath,
    [property: JsonPropertyName("tus")] TusUploadDescriptor? Tus);

public sealed record TusUploadDescriptor(
    [property: JsonPropertyName("endpoint")] string Endpoint,
    [property: JsonPropertyName("signature")] string Signature,
    [property: JsonPropertyName("chunk_size_bytes")] int ChunkSizeBytes,
    [property: JsonPropertyName("metadata")] TusUploadMetadata Metadata);

public sealed record TusUploadMetadata(
    [property: JsonPropertyName("bucketName")] string BucketName,
    [property: JsonPropertyName("objectName")] string ObjectName,
    [property: JsonPropertyName("contentType")] string ContentType,
    [property: JsonPropertyName("cacheControl")] string CacheControl);

public sealed record CompleteRecordingUploadRequest(
    [property: JsonPropertyName("file_size_bytes")] long FileSizeBytes,
    [property: JsonPropertyName("source_fingerprint")] string SourceFingerprint);

public sealed record CompleteRecordingUploadResponse(
    [property: JsonPropertyName("recording_id")] string RecordingId,
    [property: JsonPropertyName("sync_state")] string SyncState,
    [property: JsonPropertyName("uploaded_at")] DateTimeOffset UploadedAt);
