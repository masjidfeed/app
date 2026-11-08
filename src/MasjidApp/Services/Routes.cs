namespace MasjidApp.Services;

/// <summary>
/// Centralized Shell route names so navigation uses typed constants instead of magic strings.
/// </summary>
/// <remarks>
/// Absolute routes (prefixed with <c>//</c>) select a named <see cref="Microsoft.Maui.Controls.ShellContent"/>
/// in the <see cref="Microsoft.Maui.Controls.TabBar"/>.  Relative routes (no prefix) are pushed onto the
/// current tab's navigation stack via <c>Routing.RegisterRoute</c>.
/// </remarks>
public static class Routes
{
    // ── Absolute tab routes - select the named bottom-tab ──────────────────────

    /// <summary>The app's Home tab (HomeShellPage).</summary>
    public const string Main = "//home";

    /// <summary>Donations page tab (FR-DN-01–FR-DN-04).</summary>
    public const string Donations = "//donations";

    /// <summary>Qibla compass tab (FR-CM-03) - Batch B5.</summary>
    public const string Qibla = "//qibla";

    /// <summary>Prayer times tab (FR-PT-01–FR-PT-05) - Batch A4.</summary>
    public const string PrayerTimes = "//prayer";

    /// <summary>Settings &amp; personalization page - pushable from any tab (FR-ST-01–FR-ST-05).</summary>
    public const string Settings = "settings";

    /// <summary>Persistent remote notification inbox.</summary>
    public const string Notifications = "notifications";

    /// <summary>Theme selection drill-down from Settings (FR-ST-03).</summary>
    public const string Appearance = "appearance";

    /// <summary>Iqama reminder lead-time drill-down from Settings (FR-PT-10).</summary>
    public const string PrayerReminderLeadTime = "prayer-reminder-lead-time";

    // ── Relative routes - pushed onto the current tab's navigation stack ───────

    /// <summary>Unified announcement/event detail view.</summary>
    public const string PostDetail = "post-detail";

    /// <summary>Selects Home and pushes the unified post detail in one Shell navigation.</summary>
    public const string PostDetailFromHome = "//home/post-detail";

    /// <summary>Legacy event detail route retained for navigation compatibility.</summary>
    public const string EventDetail = "event-detail";

    /// <summary>Legacy announcement detail route retained for navigation compatibility.</summary>
    public const string AnnouncementDetail = "announcement-detail";

    /// <summary>Full-screen image preview for announcement and event images.</summary>
    public const string ImagePreview = "image-preview";

    /// <summary>Contact &amp; masjid info screen (FR-CM-02, FR-CM-04) - Batch B4.</summary>
    public const string Contact = "contact";

    /// <summary>Parking guide map with zone overlays and spot validation.</summary>
    public const string Parking = "parking";

    // ── Modal / overlay routes ──────────────────────────────────────────────────

    /// <summary>Masjid selection screen (accessible from Settings).</summary>
    public const string MasjidSelection = "masjid-selection";

    /// <summary>Add a new masjid by URL or QR code (FR-ST-06).</summary>
    public const string AddMasjid = "add-masjid";


    /// <summary>Privacy policy page (NFR-PR-02).</summary>
    public const string PrivacyPolicy = "privacy-policy";
}
