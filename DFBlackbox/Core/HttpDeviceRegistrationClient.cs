using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DFBlackbox.Models;

namespace DFBlackbox.Core;

public sealed class HttpDeviceRegistrationClient : IDeviceRegistrationClient, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public HttpDeviceRegistrationClient(Uri baseAddress, TimeSpan timeout, string? publishableKey = null)
        : this(CreateHttpClient(baseAddress, timeout, publishableKey), ownsHttpClient: true)
    {
    }

    public HttpDeviceRegistrationClient(HttpClient httpClient, bool ownsHttpClient = false)
    {
        _httpClient = httpClient;
        _ownsHttpClient = ownsHttpClient;
    }

    public async Task<CreateDeviceClaimResponse> CreateClaimAsync(
        CreateDeviceClaimRequest request,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.PostAsJsonAsync(
            "device-claims",
            request,
            JsonOptions,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await ReadRequiredAsync<CreateDeviceClaimResponse>(response, cancellationToken);
    }

    public async Task<DeviceClaimStatusResponse> GetClaimStatusAsync(
        string claimId,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(
            $"device-claims/{Uri.EscapeDataString(claimId)}/status",
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await ReadRequiredAsync<DeviceClaimStatusResponse>(response, cancellationToken);
    }

    public async Task ReportProvisioningResultAsync(
        string deviceId,
        string deviceToken,
        ProvisioningResultRequest request,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"devices/{Uri.EscapeDataString(deviceId)}/provisioning-result")
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", deviceToken);
        using HttpResponseMessage response = await _httpClient.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task RevokeDeviceAsync(
        string deviceId,
        string deviceToken,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"devices/{Uri.EscapeDataString(deviceId)}/revoke");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", deviceToken);
        using HttpResponseMessage response = await _httpClient.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private static Uri EnsureTrailingSlash(Uri baseAddress)
    {
        string value = baseAddress.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? baseAddress.AbsoluteUri
            : baseAddress.AbsoluteUri + "/";
        return new Uri(value, UriKind.Absolute);
    }

    private static HttpClient CreateHttpClient(
        Uri baseAddress,
        TimeSpan timeout,
        string? publishableKey)
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

    private static async Task<T> ReadRequiredAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        T? value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return value ?? throw new InvalidDataException("The registration server returned an empty response.");
    }
}
