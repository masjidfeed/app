using System.Text.Json.Serialization;

namespace MasjidApp.Core.Models;

// API-06: per-masjid configuration document returned by /config endpoint.

/// <summary>
/// Top-level configuration document for a masjid, as returned by the
/// <c>/config</c> endpoint.  All fields are optional to support
/// tolerant parsing of partial responses (NFR-RL-01).
/// </summary>
public sealed record MasjidConfig
{
    /// <summary>Masjid identity and timezone.</summary>
    [JsonPropertyName("masjid")]
    public MasjidIdentity? Masjid { get; init; }

    /// <summary>Branding assets and theme colors.</summary>
    [JsonPropertyName("branding")]
    public BrandingConfig? Branding { get; init; }

    /// <summary>Per-masjid API endpoint URLs (override environment defaults).</summary>
    [JsonPropertyName("endpoints")]
    public EndpointsConfig? Endpoints { get; init; }

    /// <summary>Masjid contact information.</summary>
    [JsonPropertyName("contact")]
    public ContactInfo? Contact { get; init; }

    /// <summary>Optional URL to open in-app for donations (FR-DN-01).</summary>
    [JsonPropertyName("donationUrl")]
    public string? DonationUrl { get; init; }

    /// <summary>Optional URL shown in-app for Ramadan programs when the ramadan flag is enabled.</summary>
    [JsonPropertyName("ramadanUrl")]
    public string? RamadanUrl { get; init; }

    /// <summary>Optional social media links.</summary>
    [JsonPropertyName("social")]
    public SocialLinks? Social { get; init; }

    /// <summary>Feature flags controlling which sections are visible.</summary>
    [JsonPropertyName("featureFlags")]
    public FeatureFlags? FeatureFlags { get; init; }

    /// <summary>Remote push registration and per-platform Firebase configuration.</summary>
    [JsonPropertyName("pushNotifications")]
    public PushNotificationsConfig? PushNotifications { get; init; }
}

/// <summary>Per-tenant remote push configuration.</summary>
public sealed record PushNotificationsConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    [JsonPropertyName("configured")]
    public bool Configured { get; init; }

    [JsonPropertyName("registrationUrl")]
    public string? RegistrationUrl { get; init; }

    [JsonPropertyName("firebase")]
    public FirebaseConfig? Firebase { get; init; }
}

/// <summary>Firebase project and platform app options for one tenant.</summary>
public sealed record FirebaseConfig
{
    [JsonPropertyName("ios")]
    public FirebaseIosConfig? Ios { get; init; }

    [JsonPropertyName("android")]
    public FirebaseAndroidConfig? Android { get; init; }
}

/// <summary>Firebase options for the iOS app.</summary>
public sealed record FirebaseIosConfig
{
    [JsonPropertyName("appId")]
    public string? AppId { get; init; }

    [JsonPropertyName("senderId")]
    public string? SenderId { get; init; }

    [JsonPropertyName("projectId")]
    public string? ProjectId { get; init; }

    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; init; }
}

/// <summary>Firebase options for the Android app.</summary>
public sealed record FirebaseAndroidConfig
{
    [JsonPropertyName("appId")]
    public string? AppId { get; init; }

    [JsonPropertyName("senderId")]
    public string? SenderId { get; init; }

    [JsonPropertyName("projectId")]
    public string? ProjectId { get; init; }

    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; init; }
}

/// <summary>Masjid identity (name, timezone, ID).</summary>
public sealed record MasjidIdentity
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>IANA timezone for all time computations (FR-PT-06).</summary>
    [JsonPropertyName("timezone")]
    public string? Timezone { get; init; }
}

/// <summary>Branding assets and theme colors for the active masjid.</summary>
public sealed record BrandingConfig
{
    [JsonPropertyName("logoUrl")]
    public string? LogoUrl { get; init; }

    [JsonPropertyName("splashLogoUrl")]
    public string? SplashLogoUrl { get; init; }

    [JsonPropertyName("primaryColor")]
    public string? PrimaryColor { get; init; }

    /// <summary>Brand accent color used when the app renders in dark mode; falls back to <see cref="PrimaryColor"/>.</summary>
    [JsonPropertyName("primaryColorDark")]
    public string? PrimaryColorDark { get; init; }
}

/// <summary>
/// Per-masjid API endpoint URLs; all are full URLs.
/// Downstream features resolve these at runtime via <c>IMasjidContext</c>.
/// </summary>
public sealed record EndpointsConfig
{
    [JsonPropertyName("salahApiUrl")]
    public string? SalahApiUrl { get; init; }

    [JsonPropertyName("eventsUrl")]
    public string? EventsUrl { get; init; }

    [JsonPropertyName("announcementsUrl")]
    public string? AnnouncementsUrl { get; init; }

    /// <summary>URL of the parking map document served by <c>GET /parking-config</c>.</summary>
    [JsonPropertyName("parkingUrl")]
    public string? ParkingUrl { get; init; }
}

/// <summary>Masjid contact details (FR-CM-04).</summary>
public sealed record ContactInfo
{
    [JsonPropertyName("address")]
    public string? Address { get; init; }

    [JsonPropertyName("phone")]
    public string? Phone { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("website")]
    public string? Website { get; init; }
}

/// <summary>Optional social-media URLs.</summary>
public sealed record SocialLinks
{
    [JsonPropertyName("facebook")]
    public string? Facebook { get; init; }

    [JsonPropertyName("instagram")]
    public string? Instagram { get; init; }

    [JsonPropertyName("whatsapp")]
    public string? WhatsApp { get; init; }
}

/// <summary>
/// Per-masjid feature flags that control UI entry points.
/// Defaults to <c>false</c> so the app degrades gracefully when fields are absent.
/// </summary>
public sealed record FeatureFlags
{
    [JsonPropertyName("events")]
    public bool Events { get; init; }

    [JsonPropertyName("announcements")]
    public bool Announcements { get; init; }

    /// <summary>When <c>false</c>, hide the remote push notifications toggle and skip notification prompts.</summary>
    [JsonPropertyName("pushNotifications")]
    public bool PushNotifications { get; init; }

    /// <summary>When <c>false</c> or donation URL is absent, hide the donations entry (FR-DN-04).</summary>
    [JsonPropertyName("donations")]
    public bool Donations { get; init; }

    [JsonPropertyName("qibla")]
    public bool Qibla { get; init; }

    [JsonPropertyName("prayerReminders")]
    public bool PrayerReminders { get; init; }

    /// <summary>When <c>true</c> and <c>ramadanUrl</c> is present, show the Ramadan home category.</summary>
    [JsonPropertyName("ramadan")]
    public bool Ramadan { get; init; }

    /// <summary>When <c>true</c> and <c>parkingUrl</c> is present, show the parking guide entry point.</summary>
    [JsonPropertyName("parking")]
    public bool Parking { get; init; }
}
