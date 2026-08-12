namespace DFBlackbox.Models;

public sealed class DeviceRegistrationSettings
{
    public const string DefaultApiBaseUrl = "https://ujttgkmwqdwxevnbblvb.supabase.co/functions/v1/device-registration/";
    public const string DefaultSupabasePublishableKey = "sb_publishable_nDFRx65ANRyHFNwDvGxSGw_fFVL-fY5";

    public string ApiBaseUrl { get; set; } = DefaultApiBaseUrl;
    public string SupabasePublishableKey { get; set; } = DefaultSupabasePublishableKey;
    public string NasRootFolder { get; set; } = "";
    public string InstallationId { get; set; } = Guid.NewGuid().ToString("N");
    public int PollIntervalSeconds { get; set; } = 3;
    public int ClaimTimeoutMinutes { get; set; } = 5;
    public string DeviceId { get; set; } = "";
    public string CameraId { get; set; } = "";
    public string NasRelativePath { get; set; } = "";
}
