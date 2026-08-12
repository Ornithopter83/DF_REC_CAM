using System.Buffers;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DFBlackbox.Models;

namespace DFBlackbox.Core;

public sealed class HttpRecordingMediaClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public HttpRecordingMediaClient(Uri baseAddress, TimeSpan timeout, string publishableKey)
        : this(CreateHttpClient(baseAddress, timeout, publishableKey), ownsHttpClient: true)
    {
    }

    public HttpRecordingMediaClient(HttpClient httpClient, bool ownsHttpClient = false)
    {
        _httpClient = httpClient;
        _ownsHttpClient = ownsHttpClient;
    }

    public async Task<BeginRecordingUploadResponse> BeginUploadAsync(
        string deviceId,
        string deviceToken,
        BeginRecordingUploadRequest request,
        CancellationToken cancellationToken)
    {
        using var message = CreateDeviceRequest(
            HttpMethod.Post,
            $"devices/{Uri.EscapeDataString(deviceId)}/recordings/upload-session",
            deviceToken);
        message.Content = JsonContent.Create(request, options: JsonOptions);
        using HttpResponseMessage response = await _httpClient.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await ReadRequiredAsync<BeginRecordingUploadResponse>(response, cancellationToken);
    }

    public async Task<CompleteRecordingUploadResponse> CompleteUploadAsync(
        string deviceId,
        string deviceToken,
        string recordingId,
        CompleteRecordingUploadRequest request,
        CancellationToken cancellationToken)
    {
        using var message = CreateDeviceRequest(
            HttpMethod.Post,
            $"devices/{Uri.EscapeDataString(deviceId)}/recordings/{Uri.EscapeDataString(recordingId)}/complete",
            deviceToken);
        message.Content = JsonContent.Create(request, options: JsonOptions);
        using HttpResponseMessage response = await _httpClient.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await ReadRequiredAsync<CompleteRecordingUploadResponse>(response, cancellationToken);
    }

    public async Task<Uri> UploadFileAsync(
        TusUploadDescriptor upload,
        string filePath,
        Uri? resumeLocation,
        Func<Uri, Task>? locationAvailable,
        CancellationToken cancellationToken)
    {
        var endpoint = new Uri(upload.Endpoint, UriKind.Absolute);
        long fileLength = new FileInfo(filePath).Length;
        (Uri location, long offset)? resumed = resumeLocation is null
            ? null
            : await TryGetUploadOffsetAsync(resumeLocation, upload.Signature, fileLength, cancellationToken);

        Uri uploadLocation;
        long uploadOffset;
        if (resumed.HasValue)
        {
            uploadLocation = resumed.Value.location;
            uploadOffset = resumed.Value.offset;
        }
        else
        {
            uploadLocation = await CreateUploadAsync(endpoint, upload, fileLength, cancellationToken);
            uploadOffset = 0;
            if (locationAvailable is not null)
            {
                await locationAvailable(uploadLocation);
            }
        }

        int chunkSize = Math.Clamp(upload.ChunkSizeBytes, 256 * 1024, 6 * 1024 * 1024);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(chunkSize);
        try
        {
            await using var source = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                chunkSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            source.Position = uploadOffset;
            while (uploadOffset < fileLength)
            {
                int requested = (int)Math.Min(chunkSize, fileLength - uploadOffset);
                int read = await ReadExactlyUpToAsync(source, buffer, requested, cancellationToken);
                if (read != requested)
                {
                    throw new EndOfStreamException("The recording changed while it was being uploaded.");
                }

                long nextOffset = await PatchChunkAsync(
                    uploadLocation,
                    upload.Signature,
                    uploadOffset,
                    buffer,
                    read,
                    cancellationToken);
                if (nextOffset != uploadOffset + read)
                {
                    throw new InvalidDataException("The resumable upload server returned an invalid offset.");
                }

                uploadOffset = nextOffset;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: false);
        }

        return uploadLocation;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    public static Uri DeriveBaseAddress(Uri deviceRegistrationBaseAddress)
    {
        Uri registrationBase = EnsureTrailingSlash(deviceRegistrationBaseAddress);
        return new Uri(registrationBase, "../recording-media/");
    }

    private async Task<(Uri location, long offset)?> TryGetUploadOffsetAsync(
        Uri location,
        string signature,
        long fileLength,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Head, location);
        AddTusHeaders(message, signature);
        using HttpResponseMessage response = await _httpClient.SendAsync(message, cancellationToken);
        if (response.StatusCode is HttpStatusCode.BadRequest
            or HttpStatusCode.Unauthorized
            or HttpStatusCode.Forbidden
            or HttpStatusCode.NotFound
            or HttpStatusCode.Gone)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        long offset = ReadUploadOffset(response);
        if (offset < 0 || offset > fileLength)
        {
            throw new InvalidDataException("The resumable upload offset is outside the recording.");
        }

        return (location, offset);
    }

    private async Task<Uri> CreateUploadAsync(
        Uri endpoint,
        TusUploadDescriptor upload,
        long fileLength,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint);
        AddTusHeaders(message, upload.Signature);
        message.Headers.TryAddWithoutValidation(
            "Upload-Length",
            fileLength.ToString(CultureInfo.InvariantCulture));
        message.Headers.TryAddWithoutValidation("Upload-Metadata", EncodeMetadata(upload.Metadata));
        using HttpResponseMessage response = await _httpClient.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();

        Uri? location = response.Headers.Location;
        if (location is null)
        {
            throw new InvalidDataException("The resumable upload server did not return a location.");
        }

        return location.IsAbsoluteUri ? location : new Uri(endpoint, location);
    }

    private async Task<long> PatchChunkAsync(
        Uri location,
        string signature,
        long offset,
        byte[] buffer,
        int count,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Patch, location);
        AddTusHeaders(message, signature);
        message.Headers.TryAddWithoutValidation(
            "Upload-Offset",
            offset.ToString(CultureInfo.InvariantCulture));
        message.Content = new ByteArrayContent(buffer, 0, count);
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/offset+octet-stream");
        using HttpResponseMessage response = await _httpClient.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();
        return ReadUploadOffset(response);
    }

    private static void AddTusHeaders(HttpRequestMessage message, string signature)
    {
        message.Headers.TryAddWithoutValidation("Tus-Resumable", "1.0.0");
        message.Headers.TryAddWithoutValidation("x-signature", signature);
    }

    private static long ReadUploadOffset(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Upload-Offset", out IEnumerable<string>? values)
            || !long.TryParse(values.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out long offset))
        {
            throw new InvalidDataException("The resumable upload server did not return a valid offset.");
        }

        return offset;
    }

    private static string EncodeMetadata(TusUploadMetadata metadata) => string.Join(",",
        Pair("bucketName", metadata.BucketName),
        Pair("objectName", metadata.ObjectName),
        Pair("contentType", metadata.ContentType),
        Pair("cacheControl", metadata.CacheControl));

    private static string Pair(string key, string value) =>
        $"{key} {Convert.ToBase64String(Encoding.UTF8.GetBytes(value))}";

    private static async Task<int> ReadExactlyUpToAsync(
        Stream stream,
        byte[] buffer,
        int count,
        CancellationToken cancellationToken)
    {
        int total = 0;
        while (total < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(total, count - total), cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private HttpRequestMessage CreateDeviceRequest(HttpMethod method, string relativeUri, string deviceToken)
    {
        var message = new HttpRequestMessage(method, relativeUri);
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", deviceToken);
        return message;
    }

    private static HttpClient CreateHttpClient(Uri baseAddress, TimeSpan timeout, string publishableKey)
    {
        var client = new HttpClient
        {
            BaseAddress = EnsureTrailingSlash(baseAddress),
            Timeout = timeout
        };
        if (!string.IsNullOrWhiteSpace(publishableKey))
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("apikey", publishableKey.Trim());
        }

        return client;
    }

    private static Uri EnsureTrailingSlash(Uri uri) => uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
        ? uri
        : new Uri(uri.AbsoluteUri + "/", UriKind.Absolute);

    private static async Task<T> ReadRequiredAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        T? value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return value ?? throw new InvalidDataException("The recording server returned an empty response.");
    }
}
