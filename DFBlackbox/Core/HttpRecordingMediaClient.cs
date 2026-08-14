using System.Net.Http.Json;
using System.Net.Http.Headers;
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

    private static Uri EnsureTrailingSlash(Uri uri) => uri.AbsoluteUri.EndsWith('/')
        ? uri
        : new Uri(uri.AbsoluteUri + "/", UriKind.Absolute);
}
