using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DFBlackbox.Models;

namespace DFBlackbox.Core;

public sealed class HttpMediaSessionClient : IMediaSessionClient, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public HttpMediaSessionClient(
        Uri deviceRegistrationApiBaseAddress,
        TimeSpan timeout,
        string publishableKey)
    {
        if (string.IsNullOrWhiteSpace(publishableKey))
        {
            throw new ArgumentException("Supabase publishable key is required.", nameof(publishableKey));
        }

        _httpClient = new HttpClient
        {
            BaseAddress = ResolveSiblingFunctionBaseAddress(deviceRegistrationApiBaseAddress, "media-session"),
            Timeout = timeout
        };
        _httpClient.DefaultRequestHeaders.Add("apikey", publishableKey.Trim());
        _ownsHttpClient = true;
    }

    public HttpMediaSessionClient(HttpClient httpClient, bool ownsHttpClient = false)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsHttpClient = ownsHttpClient;
    }

    public async Task<DeviceStreamCommand> GetDeviceStreamCommandAsync(
        string deviceId,
        string deviceToken,
        CancellationToken cancellationToken)
    {
        ValidateUuid(deviceId, nameof(deviceId));
        using var request = CreateRequest(
            HttpMethod.Get,
            $"devices/{Uri.EscapeDataString(deviceId)}/stream-command",
            deviceToken);
        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        DeviceCommandResponse? body = await response.Content.ReadFromJsonAsync<DeviceCommandResponse>(
            JsonOptions,
            cancellationToken);
        if (body is null)
        {
            throw new MediaSessionException("invalid_stream_command", response.StatusCode);
        }

        if (body.ShouldStream
            && (string.IsNullOrWhiteSpace(body.CameraId)
                || string.IsNullOrWhiteSpace(body.IngressUrl)
                || string.IsNullOrWhiteSpace(body.IngressStreamKey)
                || body.LeaseUntil is null))
        {
            throw new MediaSessionException("invalid_stream_command", response.StatusCode);
        }

        return new DeviceStreamCommand(
            body.CameraId,
            body.RoomName,
            body.ShouldStream,
            body.IngressUrl,
            body.IngressStreamKey,
            body.LeaseUntil);
    }

    public async Task ReportDeviceStreamStateAsync(
        string deviceId,
        string deviceToken,
        string cameraId,
        DeviceStreamState state,
        string? errorCode,
        CancellationToken cancellationToken)
    {
        ValidateUuid(deviceId, nameof(deviceId));
        ValidateUuid(cameraId, nameof(cameraId));
        var payload = new DeviceStateRequest(
            cameraId,
            state switch
            {
                DeviceStreamState.Idle => "idle",
                DeviceStreamState.Publishing => "publishing",
                DeviceStreamState.Error => "error",
                _ => throw new ArgumentOutOfRangeException(nameof(state))
            },
            state == DeviceStreamState.Error ? NormalizeErrorCode(errorCode) : null);
        using var request = CreateRequest(
            HttpMethod.Post,
            $"devices/{Uri.EscapeDataString(deviceId)}/stream-state",
            deviceToken);
        request.Content = JsonContent.Create(payload, options: JsonOptions);
        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    public static Uri ResolveSiblingFunctionBaseAddress(Uri registrationBaseAddress, string functionName)
    {
        ArgumentNullException.ThrowIfNull(registrationBaseAddress);
        if (!registrationBaseAddress.IsAbsoluteUri)
        {
            throw new ArgumentException("An absolute registration API URL is required.", nameof(registrationBaseAddress));
        }

        if (string.IsNullOrWhiteSpace(functionName))
        {
            throw new ArgumentException("Function name is required.", nameof(functionName));
        }

        var builder = new UriBuilder(registrationBaseAddress)
        {
            Query = "",
            Fragment = ""
        };
        string path = builder.Path.TrimEnd('/');
        int separator = path.LastIndexOf('/');
        if (separator < 0)
        {
            throw new ArgumentException("The registration API URL has no function path.", nameof(registrationBaseAddress));
        }

        builder.Path = $"{path[..(separator + 1)]}{functionName.Trim().Trim('/')}/";
        return builder.Uri;
    }

    private static void ValidateUuid(string value, string parameterName)
    {
        if (!Guid.TryParse(value, out _))
        {
            throw new ArgumentException("A valid UUID is required.", parameterName);
        }
    }

    private static string NormalizeErrorCode(string? errorCode)
    {
        string normalized = string.IsNullOrWhiteSpace(errorCode)
            ? "stream_publish_failed"
            : errorCode.Trim();
        return normalized.Length <= 80 ? normalized : normalized[..80];
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string path, string deviceToken)
    {
        if (string.IsNullOrWhiteSpace(deviceToken))
        {
            throw new ArgumentException("Device token is required.", nameof(deviceToken));
        }

        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", deviceToken.Trim());
        return request;
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string errorCode = "media_session_request_failed";
        try
        {
            ErrorResponse? error = await response.Content.ReadFromJsonAsync<ErrorResponse>(
                JsonOptions,
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(error?.ErrorCode))
            {
                errorCode = NormalizeErrorCode(error.ErrorCode);
            }
        }
        catch (JsonException)
        {
        }

        throw new MediaSessionException(errorCode, response.StatusCode);
    }

    private sealed record DeviceCommandResponse(
        [property: JsonPropertyName("camera_id")] string? CameraId,
        [property: JsonPropertyName("room_name")] string? RoomName,
        [property: JsonPropertyName("should_stream")] bool ShouldStream,
        [property: JsonPropertyName("ingress_url")] string? IngressUrl,
        [property: JsonPropertyName("ingress_stream_key")] string? IngressStreamKey,
        [property: JsonPropertyName("lease_until")] DateTimeOffset? LeaseUntil);

    private sealed record DeviceStateRequest(
        [property: JsonPropertyName("camera_id")] string CameraId,
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("error_code")] string? ErrorCode);

    private sealed record ErrorResponse(
        [property: JsonPropertyName("error_code")] string? ErrorCode);
}

public sealed class MediaSessionException : Exception
{
    public MediaSessionException(string errorCode, HttpStatusCode statusCode)
        : base($"Media session request failed ({errorCode}, HTTP {(int)statusCode}).")
    {
        ErrorCode = errorCode;
        StatusCode = statusCode;
    }

    public string ErrorCode { get; }
    public HttpStatusCode StatusCode { get; }
}
