using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;
using DFBlackbox.Models;

namespace DFBlackbox.Core;

public sealed class HttpRecordingMediaClient : IDisposable
{
    private const int MaximumChunkSizeBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly HttpClient _nasHttpClient;
    private readonly bool _ownsHttpClient;

    public HttpRecordingMediaClient(Uri baseAddress, TimeSpan timeout, string publishableKey)
        : this(CreateHttpClient(baseAddress, timeout, publishableKey), ownsHttpClient: true)
    {
    }

    public HttpRecordingMediaClient(HttpClient httpClient, bool ownsHttpClient = false)
    {
        _httpClient = httpClient;
        _nasHttpClient = new HttpClient
        {
            Timeout = httpClient.Timeout
        };
        _ownsHttpClient = ownsHttpClient;
    }

    public async Task<RegisterRecordingCatalogResponse> RegisterCatalogAsync(
        string deviceId,
        string deviceToken,
        RegisterRecordingCatalogRequest request,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"devices/{Uri.EscapeDataString(deviceId)}/recordings/catalog");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", deviceToken);
        message.Content = JsonContent.Create(request, options: JsonOptions);
        using HttpResponseMessage response = await _httpClient.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();
        RegisterRecordingCatalogResponse? value =
            await response.Content.ReadFromJsonAsync<RegisterRecordingCatalogResponse>(JsonOptions, cancellationToken);
        return value ?? throw new InvalidDataException("The recording server returned an empty response.");
    }

    public async Task<CreateNasUploadSessionResponse> CreateNasUploadSessionAsync(
        string deviceId,
        string deviceToken,
        string cameraId,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"devices/{Uri.EscapeDataString(deviceId)}/nas-upload-session");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", deviceToken);
        message.Content = JsonContent.Create(new CreateNasUploadSessionRequest(cameraId), options: JsonOptions);
        using HttpResponseMessage response = await _httpClient.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();
        CreateNasUploadSessionResponse? value =
            await response.Content.ReadFromJsonAsync<CreateNasUploadSessionResponse>(JsonOptions, cancellationToken);
        if (value is null
            || string.IsNullOrWhiteSpace(value.Assertion)
            || value.ChunkSizeBytes <= 0
            || value.SessionExpiresAt <= DateTimeOffset.UtcNow
            || !Uri.TryCreate(value.GatewayBaseUrl, UriKind.Absolute, out Uri? gatewayBase)
            || !Uri.TryCreate(value.UploadUrl, UriKind.Absolute, out Uri? uploadUri)
            || !string.Equals(gatewayBase.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uploadUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(gatewayBase.Host, uploadUri.Host, StringComparison.OrdinalIgnoreCase)
            || gatewayBase.Port != uploadUri.Port)
        {
            throw new InvalidDataException("The recording server returned an invalid NAS upload session.");
        }

        return value;
    }

    public async Task<string> UploadToNasAsync(
        CreateNasUploadSessionResponse session,
        string filePath,
        string relativePath,
        string expectedNasRelativePath,
        long fileSizeBytes,
        string sourceFingerprint,
        DateTimeOffset sourceLastModifiedAt,
        CancellationToken cancellationToken)
    {
        var uploadUri = new Uri(session.UploadUrl, UriKind.Absolute);
        using var create = new HttpRequestMessage(HttpMethod.Post, uploadUri);
        create.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Assertion);
        create.Content = JsonContent.Create(
            new CreateNasUploadRequest(relativePath, fileSizeBytes, sourceFingerprint, sourceLastModifiedAt),
            options: JsonOptions);
        using HttpResponseMessage createResponse = await _nasHttpClient.SendAsync(create, cancellationToken);
        createResponse.EnsureSuccessStatusCode();
        NasUploadResponse upload = await ReadNasUploadResponseAsync(createResponse, cancellationToken);
        ValidateUploadResponse(upload, fileSizeBytes);

        if (!string.Equals(upload.State, "complete", StringComparison.OrdinalIgnoreCase))
        {
            int chunkSize = Math.Min(Math.Clamp(session.ChunkSizeBytes, 1, MaximumChunkSizeBytes), MaximumChunkSizeBytes);
            byte[] buffer = new byte[chunkSize];
            await using var source = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                chunkSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            source.Position = upload.Offset;
            while (source.Position < fileSizeBytes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int requested = (int)Math.Min(buffer.Length, fileSizeBytes - source.Position);
                int read = await source.ReadAsync(buffer.AsMemory(0, requested), cancellationToken);
                if (read <= 0)
                {
                    throw new EndOfStreamException("The local recording ended before the declared file size.");
                }

                long offset = source.Position - read;
                using var append = new HttpRequestMessage(
                    HttpMethod.Patch,
                    BuildUploadUri(uploadUri, upload.UploadId));
                append.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Assertion);
                append.Headers.TryAddWithoutValidation("Upload-Offset", offset.ToString(System.Globalization.CultureInfo.InvariantCulture));
                var content = new ByteArrayContent(buffer, 0, read);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/offset+octet-stream");
                append.Content = content;
                using HttpResponseMessage appendResponse = await _nasHttpClient.SendAsync(append, cancellationToken);
                appendResponse.EnsureSuccessStatusCode();
                long confirmedOffset = ReadRequiredInt64Header(appendResponse, "Upload-Offset");
                if (confirmedOffset != source.Position)
                {
                    throw new InvalidDataException("The NAS returned an unexpected upload offset.");
                }
            }

            using var complete = new HttpRequestMessage(HttpMethod.Post, BuildUploadUri(uploadUri, upload.UploadId));
            complete.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Assertion);
            using HttpResponseMessage completeResponse = await _nasHttpClient.SendAsync(complete, cancellationToken);
            completeResponse.EnsureSuccessStatusCode();
            upload = await ReadNasUploadResponseAsync(completeResponse, cancellationToken);
            ValidateUploadResponse(upload, fileSizeBytes);
        }

        return upload.NasRelativePath ?? expectedNasRelativePath;
    }

    public void Dispose()
    {
        _nasHttpClient.Dispose();
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

    private static HttpClient CreateHttpClient(Uri baseAddress, TimeSpan timeout, string publishableKey)
    {
        var httpClient = new HttpClient
        {
            BaseAddress = EnsureTrailingSlash(baseAddress),
            Timeout = timeout
        };
        if (!string.IsNullOrWhiteSpace(publishableKey))
        {
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("apikey", publishableKey.Trim());
        }

        return httpClient;
    }

    private static async Task<NasUploadResponse> ReadNasUploadResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        NasUploadResponse? value =
            await response.Content.ReadFromJsonAsync<NasUploadResponse>(JsonOptions, cancellationToken);
        return value ?? throw new InvalidDataException("The NAS returned an empty upload response.");
    }

    private static void ValidateUploadResponse(NasUploadResponse response, long expectedLength)
    {
        if (response.UploadId.Length != 64
            || !response.UploadId.All(Uri.IsHexDigit)
            || response.Length != expectedLength
            || response.Offset < 0
            || response.Offset > expectedLength
            || (!string.Equals(response.State, "uploading", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(response.State, "complete", StringComparison.OrdinalIgnoreCase))
            || (string.Equals(response.State, "complete", StringComparison.OrdinalIgnoreCase)
                && response.Offset != expectedLength))
        {
            throw new InvalidDataException("The NAS returned invalid upload state.");
        }
    }

    private static Uri BuildUploadUri(Uri uploadUri, string uploadId)
    {
        var builder = new UriBuilder(uploadUri) { Query = $"upload={Uri.EscapeDataString(uploadId)}" };
        return builder.Uri;
    }

    private static long ReadRequiredInt64Header(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues(name, out IEnumerable<string>? values)
            || !long.TryParse(values.SingleOrDefault(), out long value))
        {
            throw new InvalidDataException($"The NAS response omitted {name}.");
        }

        return value;
    }

    private static Uri EnsureTrailingSlash(Uri uri) => uri.AbsoluteUri.EndsWith('/')
        ? uri
        : new Uri(uri.AbsoluteUri + "/", UriKind.Absolute);
}
