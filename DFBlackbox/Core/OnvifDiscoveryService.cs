using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;

namespace DFBlackbox.Core;

public sealed record OnvifCameraDiscoveryResult(
    string IpAddress,
    int? HttpPort,
    string? OnvifServiceUrl,
    string? MediaServiceUrl,
    string? ProfileName,
    string? ProfileToken,
    string? VideoEncoding,
    int? Width,
    int? Height,
    string? RtspUri)
{
    public override string ToString()
    {
        string port = HttpPort.HasValue ? $":{HttpPort.Value}" : "";
        string stream = string.IsNullOrWhiteSpace(RtspUri) ? "no RTSP URI" : RtspUri;
        string profile = string.IsNullOrWhiteSpace(ProfileName) ? "" : $" / {ProfileName}";
        return $"{IpAddress}{port}{profile} / {stream}";
    }
}

public sealed class OnvifDiscoveryService
{
    private static readonly XNamespace Soap = "http://www.w3.org/2003/05/soap-envelope";
    private static readonly XNamespace Addressing = "http://www.w3.org/2005/08/addressing";
    private static readonly XNamespace Discovery = "http://schemas.xmlsoap.org/ws/2005/04/discovery";
    private static readonly XNamespace Device = "http://www.onvif.org/ver10/device/wsdl";
    private static readonly XNamespace Media = "http://www.onvif.org/ver10/media/wsdl";
    private static readonly XNamespace Schema = "http://www.onvif.org/ver10/schema";
    private readonly HttpClient _httpClient;

    public OnvifDiscoveryService()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
    }

    public async Task<List<OnvifCameraDiscoveryResult>> DiscoverAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var xaddrs = await ProbeAsync(timeout, cancellationToken);
        var results = new List<OnvifCameraDiscoveryResult>();

        foreach (var xaddr in xaddrs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await TryBuildResultAsync(xaddr, cancellationToken);
            if (result is not null && results.All(item => !SameCamera(item, result)))
            {
                results.Add(result);
            }
        }

        return results;
    }

    private static bool SameCamera(OnvifCameraDiscoveryResult left, OnvifCameraDiscoveryResult right)
    {
        return string.Equals(left.IpAddress, right.IpAddress, StringComparison.OrdinalIgnoreCase)
            && left.HttpPort == right.HttpPort
            && string.Equals(left.RtspUri, right.RtspUri, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<List<string>> ProbeAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        string probe = BuildProbeMessage();
        byte[] payload = Encoding.UTF8.GetBytes(probe);
        var responses = new List<string>();
        DateTimeOffset deadline = DateTimeOffset.UtcNow.Add(timeout);
        using var client = new UdpClient(AddressFamily.InterNetwork);

        client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        client.Client.ReceiveTimeout = 500;
        client.EnableBroadcast = true;

        foreach (var address in GetLocalIPv4Addresses())
        {
            try
            {
                client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, address.GetAddressBytes());
                await client.SendAsync(payload, payload.Length, new IPEndPoint(IPAddress.Parse("239.255.255.250"), 3702));
            }
            catch
            {
                // Some adapters reject multicast binding; other active adapters can still answer.
            }
        }

        try
        {
            await client.SendAsync(payload, payload.Length, new IPEndPoint(IPAddress.Parse("239.255.255.250"), 3702));
        }
        catch
        {
            return responses;
        }

        while (DateTimeOffset.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var receiveTask = client.ReceiveAsync(cancellationToken).AsTask();
                var delayTask = Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
                var completed = await Task.WhenAny(receiveTask, delayTask);
                if (completed != receiveTask)
                {
                    continue;
                }

                string text = Encoding.UTF8.GetString(receiveTask.Result.Buffer);
                foreach (var xaddr in ParseXAddrs(text))
                {
                    if (!responses.Contains(xaddr, StringComparer.OrdinalIgnoreCase))
                    {
                        responses.Add(xaddr);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Ignore malformed UDP responses and keep listening until the short deadline.
            }
        }

        return responses;
    }

    private async Task<OnvifCameraDiscoveryResult?> TryBuildResultAsync(string deviceServiceUrl, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(deviceServiceUrl, UriKind.Absolute, out var deviceUri))
        {
            return null;
        }

        string mediaUrl = await TryGetMediaServiceUrlAsync(deviceServiceUrl, cancellationToken) ?? deviceServiceUrl;
        var profiles = await TryGetProfilesAsync(mediaUrl, cancellationToken);
        var profile = profiles.FirstOrDefault();
        var rtspUri = profile is null ? null : await TryGetStreamUriAsync(mediaUrl, profile.Token, cancellationToken);

        return new OnvifCameraDiscoveryResult(
            deviceUri.Host,
            deviceUri.IsDefaultPort ? null : deviceUri.Port,
            deviceServiceUrl,
            mediaUrl,
            profile?.Name,
            profile?.Token,
            profile?.Encoding,
            profile?.Width,
            profile?.Height,
            rtspUri);
    }

    private async Task<string?> TryGetMediaServiceUrlAsync(string deviceServiceUrl, CancellationToken cancellationToken)
    {
        var response = await TryPostSoapAsync(deviceServiceUrl, BuildGetCapabilitiesMessage(), "http://www.onvif.org/ver10/device/wsdl/GetCapabilities", cancellationToken);
        if (response is null)
        {
            return null;
        }

        return XDocument.Parse(response)
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "XAddr" && element.Parent?.Name.LocalName == "Media")
            ?.Value
            .Trim();
    }

    private async Task<List<OnvifProfile>> TryGetProfilesAsync(string mediaServiceUrl, CancellationToken cancellationToken)
    {
        var response = await TryPostSoapAsync(mediaServiceUrl, BuildGetProfilesMessage(), "http://www.onvif.org/ver10/media/wsdl/GetProfiles", cancellationToken);
        if (response is null)
        {
            return [];
        }

        var document = XDocument.Parse(response);
        return document.Descendants()
            .Where(element => element.Name.LocalName == "Profiles")
            .Select(ParseProfile)
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Token))
            .ToList();
    }

    private static OnvifProfile ParseProfile(XElement profileElement)
    {
        string token = profileElement.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "token")?.Value ?? "";
        string? name = profileElement.Elements().FirstOrDefault(element => element.Name.LocalName == "Name")?.Value.Trim();
        var encoder = profileElement.Descendants().FirstOrDefault(element => element.Name.LocalName == "VideoEncoderConfiguration");
        var encoding = encoder?.Elements().FirstOrDefault(element => element.Name.LocalName == "Encoding")?.Value.Trim();
        var resolution = encoder?.Descendants().FirstOrDefault(element => element.Name.LocalName == "Resolution");
        var width = TryParseInt(resolution?.Elements().FirstOrDefault(element => element.Name.LocalName == "Width")?.Value);
        var height = TryParseInt(resolution?.Elements().FirstOrDefault(element => element.Name.LocalName == "Height")?.Value);

        return new OnvifProfile(token, name, encoding, width, height);
    }

    private async Task<string?> TryGetStreamUriAsync(string mediaServiceUrl, string profileToken, CancellationToken cancellationToken)
    {
        var response = await TryPostSoapAsync(mediaServiceUrl, BuildGetStreamUriMessage(profileToken), "http://www.onvif.org/ver10/media/wsdl/GetStreamUri", cancellationToken);
        if (response is null)
        {
            return null;
        }

        return XDocument.Parse(response)
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "Uri")
            ?.Value
            .Trim();
    }

    private async Task<string?> TryPostSoapAsync(string url, string body, string action, CancellationToken cancellationToken)
    {
        try
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/soap+xml");
            content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse($"application/soap+xml; charset=utf-8; action=\"{action}\"");
            using var response = await _httpClient.PostAsync(url, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<IPAddress> GetLocalIPv4Addresses()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
            .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
            .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address.Address))
            .Select(address => address.Address);
    }

    private static IEnumerable<string> ParseXAddrs(string response)
    {
        var document = XDocument.Parse(response);
        return document.Descendants()
            .Where(element => element.Name.LocalName == "XAddrs")
            .SelectMany(element => element.Value.Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries))
            .Where(value => Uri.TryCreate(value, UriKind.Absolute, out _));
    }

    private static string BuildProbeMessage()
    {
        return new XDocument(
            new XElement(Soap + "Envelope",
                new XAttribute(XNamespace.Xmlns + "s", Soap),
                new XAttribute(XNamespace.Xmlns + "a", Addressing),
                new XAttribute(XNamespace.Xmlns + "d", Discovery),
                new XAttribute(XNamespace.Xmlns + "dn", "http://www.onvif.org/ver10/network/wsdl"),
                new XElement(Soap + "Header",
                    new XElement(Addressing + "Action", "http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe"),
                    new XElement(Addressing + "MessageID", $"uuid:{Guid.NewGuid()}"),
                    new XElement(Addressing + "To", "urn:schemas-xmlsoap-org:ws:2005:04:discovery")),
                new XElement(Soap + "Body",
                    new XElement(Discovery + "Probe",
                        new XElement(Discovery + "Types", "dn:NetworkVideoTransmitter"))))).ToString(SaveOptions.DisableFormatting);
    }

    private static string BuildGetCapabilitiesMessage()
    {
        return new XDocument(
            new XElement(Soap + "Envelope",
                new XAttribute(XNamespace.Xmlns + "s", Soap),
                new XAttribute(XNamespace.Xmlns + "tds", Device),
                new XElement(Soap + "Body",
                    new XElement(Device + "GetCapabilities",
                        new XElement(Device + "Category", "Media"))))).ToString(SaveOptions.DisableFormatting);
    }

    private static string BuildGetProfilesMessage()
    {
        return new XDocument(
            new XElement(Soap + "Envelope",
                new XAttribute(XNamespace.Xmlns + "s", Soap),
                new XAttribute(XNamespace.Xmlns + "trt", Media),
                new XElement(Soap + "Body",
                    new XElement(Media + "GetProfiles")))).ToString(SaveOptions.DisableFormatting);
    }

    private static string BuildGetStreamUriMessage(string profileToken)
    {
        return new XDocument(
            new XElement(Soap + "Envelope",
                new XAttribute(XNamespace.Xmlns + "s", Soap),
                new XAttribute(XNamespace.Xmlns + "trt", Media),
                new XAttribute(XNamespace.Xmlns + "tt", Schema),
                new XElement(Soap + "Body",
                    new XElement(Media + "GetStreamUri",
                        new XElement(Media + "StreamSetup",
                            new XElement(Schema + "Stream", "RTP-Unicast"),
                            new XElement(Schema + "Transport",
                                new XElement(Schema + "Protocol", "RTSP"))),
                        new XElement(Media + "ProfileToken", profileToken))))).ToString(SaveOptions.DisableFormatting);
    }

    private static int? TryParseInt(string? value)
    {
        return int.TryParse(value, out var parsed) ? parsed : null;
    }

    private sealed record OnvifProfile(string Token, string? Name, string? Encoding, int? Width, int? Height);
}
