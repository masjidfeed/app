using MasjidApp.Core.Configuration;
using MasjidApp.Core.Models;
using MasjidApp.Core.Services;
using MasjidApp.Core.Theming;
using MasjidApp.Core.Utils;
using MasjidApp.Push;
using MasjidApp.Services;
using MasjidApp.Views;

namespace MasjidApp;

// API-06, API-10, FR-DN-04: AppShell wires up routes and applies feature-flag tab visibility.

/// <summary>
/// Application shell - defines the bottom tab bar (Home, Donate, Prayer, Qibla)
/// and registers all pushable routes.  Settings is accessible via the header icon
/// on the Home page.  Donate tab visibility is controlled at runtime by the active
/// masjid's <see cref="FeatureFlags"/> and donation URL (<c>FR-DN-04</c>, <c>API-06</c>).
/// </summary>
public partial class AppShell : Shell
{
    private const string DefaultPrimaryColor = "#512BD4";

    // Cache keys for the last-applied brand colors, namespaced by masjid id so a
    // switch to a different masjid doesn't briefly show a stale tenant's color.
    private const string CachedMasjidIdPreferenceKey = "masjidapp_cached_brand_masjid_id";
    private const string CachedPrimaryColorPreferenceKey = "masjidapp_cached_brand_primary_color";
    private const string CachedPrimaryColorDarkPreferenceKey = "masjidapp_cached_brand_primary_color_dark";

    private readonly IMasjidContext _context;
    private readonly IAppIconService _appIconService;
    private readonly IDeepLinkRouter _deepLinkRouter;

    // Toggling Tab.IsVisible on the Android Shell while its bottom-tab fragments are
    // still being committed throws "No view found for id .../jumpToStart". We therefore
    // defer the first visibility pass until the Shell is Loaded, and only apply changes
    // on the main thread after the initial fragment transaction has settled (API-06).
    private bool _isLoaded;

    // Latest brand accent colors from the active masjid's config (API-06, FR-ST-03).
    // The dark-mode value falls back to the light value when the tenant omits it.
    private string _primaryColor = DefaultPrimaryColor;
    private string _primaryColorDark = DefaultPrimaryColor;

    /// <summary>Creates the shell with the DI-resolved masjid context.</summary>
    public AppShell(
        IMasjidContext context,
        IAppIconService appIconService,
        IDeepLinkRouter deepLinkRouter)
    {
        _context = context;
        _appIconService = appIconService;
        _deepLinkRouter = deepLinkRouter;

        // Seed the brand colors from the last-applied cache (if it matches the active
        // masjid) and apply them *before* InitializeComponent parses the TabBar style.
        // Otherwise the tab bar always paints with the hard-coded purple default first -
        // and on iOS, Shell's native tab bar does not reliably repaint itself just because
        // a DynamicResource changes later, so the wrong color can persist until the user
        // navigates. Applying the correct color up front avoids that flash entirely.
        LoadCachedBrandColors();
        ApplyBrandColor();

        InitializeComponent();
        RegisterRoutes();

        // Apply feature flags only once the Shell (and its native tab fragments) exist.
        Loaded += OnShellLoaded;

        // Subscribe to masjid-switch so tabs update when the user changes masjid.
        _context.ActiveMasjidChanged += OnActiveMasjidChanged;

        // Re-resolve the brand accent whenever the effective theme flips (FR-ST-03).
        if (Application.Current is { } app)
        {
            app.RequestedThemeChanged += OnRequestedThemeChanged;
        }
    }

    /// <summary>
    /// Restores <see cref="_primaryColor"/>/<see cref="_primaryColorDark"/> from the last
    /// successful application, but only when the cache belongs to the currently active
    /// masjid (a masjid switch must not carry over the previous tenant's brand color).
    /// </summary>
    private void LoadCachedBrandColors()
    {
        var activeId = _context.ActiveEntry?.Id;
        if (activeId is null)
        {
            return;
        }

        var cachedId = Preferences.Default.Get<string?>(CachedMasjidIdPreferenceKey, null);
        if (cachedId != activeId)
        {
            return;
        }

        _primaryColor = Preferences.Default.Get(CachedPrimaryColorPreferenceKey, DefaultPrimaryColor);
        _primaryColorDark = Preferences.Default.Get(CachedPrimaryColorDarkPreferenceKey, DefaultPrimaryColor);
    }

    /// <summary>Persists the current brand colors so the next launch can apply them instantly.</summary>
    private void CacheBrandColors()
    {
        var activeId = _context.ActiveEntry?.Id;
        if (activeId is null)
        {
            return;
        }

        Preferences.Default.Set(CachedMasjidIdPreferenceKey, activeId);
        Preferences.Default.Set(CachedPrimaryColorPreferenceKey, _primaryColor);
        Preferences.Default.Set(CachedPrimaryColorDarkPreferenceKey, _primaryColorDark);
    }

    private void OnRequestedThemeChanged(object? sender, AppThemeChangedEventArgs e)
        => MainThread.BeginInvokeOnMainThread(ApplyBrandColor);

    private void OnShellLoaded(object? sender, EventArgs e)
    {
        _isLoaded = true;
        _ = UpdateTabVisibilityAsync();
        _ = _deepLinkRouter.RoutePendingAsync();
    }

    private void OnActiveMasjidChanged(object? sender, EventArgs e)
    {
        _ = _appIconService.ApplyAsync(TenantBranding.Get(_context.ActiveEntry));

        // Ignore switches that arrive before the Shell is loaded; OnShellLoaded
        // performs the initial pass for the already-active masjid.
        if (_isLoaded)
        {
            _ = UpdateTabVisibilityAsync();
        }
    }

    // ─── Route registration ─────────────────────────────────────────────────────

    private static void RegisterRoutes()
    {
        // Modal overlay routes (Batch 02).
        Routing.RegisterRoute(Routes.MasjidSelection, typeof(MasjidSelectionPage));
        Routing.RegisterRoute(Routes.AddMasjid, typeof(AddMasjidPage));
        Routing.RegisterRoute(Routes.PrivacyPolicy, typeof(PrivacyPolicyPage));

        // Pushable feature routes - these are navigated to from within the Home tab's
        // navigation stack (category strip, deep links from notifications, etc.).
        // They are NOT tabs in the TabBar.
        Routing.RegisterRoute(Routes.Settings, typeof(SettingsPage));
        Routing.RegisterRoute(Routes.Notifications, typeof(NotificationsPage));
        Routing.RegisterRoute(Routes.Appearance, typeof(AppearancePage));
        Routing.RegisterRoute(Routes.PrayerReminderLeadTime, typeof(PrayerReminderLeadTimePage));
        Routing.RegisterRoute(Routes.PostDetail, typeof(PostDetailPage));
        Routing.RegisterRoute(Routes.EventDetail, typeof(PostDetailPage));
        Routing.RegisterRoute(Routes.AnnouncementDetail, typeof(PostDetailPage));
        Routing.RegisterRoute(Routes.ImagePreview, typeof(ImagePreviewPage));
        Routing.RegisterRoute(Routes.Contact, typeof(ContactPage));
    }

    // ─── Feature-flag tab visibility (API-06, FR-DN-04) ─────────────────────────

    private async Task UpdateTabVisibilityAsync()
    {
        MasjidConfig? config = null;

        try
        {
            var result = await _context.GetConfigAsync().ConfigureAwait(false);
            config = result?.ValueOrDefault;
        }
        catch (Exception)
        {
            // Config fetch failed - leave tabs in their current state.
            return;
        }

        var showDonate = DonationNavigationPolicy.GetSafeDonationUri(config?.DonationUrl) is not null
                         && (config?.FeatureFlags?.Donations ?? false);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            _primaryColor = BrandColor.Normalize(config?.Branding?.PrimaryColor) ?? DefaultPrimaryColor;
            _primaryColorDark = BrandColor.Normalize(config?.Branding?.PrimaryColorDark) ?? _primaryColor;
            ApplyBrandColor();
            CacheBrandColors();

            // Only flip when the value actually changes - re-asserting the current
            // visibility can re-enter the Android fragment transaction and crash.
            if (DonateTab.IsVisible != showDonate)
            {
                DonateTab.IsVisible = showDonate;
            }
        });
    }

    /// <summary>
    /// Publishes the active masjid's accent color as the <c>ActivePrimaryColor</c>
    /// application resource for the current theme, plus a tinted <c>ActivePrimarySoftColor</c>
    /// surface used for highlighted rows.  Brand-accented UI binds them with
    /// <c>DynamicResource</c> so they re-resolve on every masjid or theme change.
    /// </summary>
    private void ApplyBrandColor()
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        var isDark = app.RequestedTheme == AppTheme.Dark;
        var primary = Color.FromArgb(isDark ? _primaryColorDark : _primaryColor);

        app.Resources["ActivePrimaryColor"] = primary;

        // In light mode the highlight is the brand color washed toward white; in dark mode
        // it is pulled toward a mid gray so the band stays lighter than the page background.
        app.Resources["ActivePrimarySoftColor"] = isDark
            ? Blend(primary, Color.FromArgb("#404040"), 0.62f)
            : Blend(primary, Colors.White, 0.86f);

        // Set the tab bar tint directly on the Shell instance rather than relying solely
        // on the Styles.xaml DynamicResource setters. Native Shell tab bar chrome (iOS in
        // particular) does not always repaint on a bare resource-dictionary change, so
        // pushing the value straight onto the property guarantees the native tracker sees
        // the update immediately instead of waiting for the next navigation.
        SetTabBarForegroundColor(this, primary);
        SetTabBarTitleColor(this, primary);
    }

    /// <summary>Linearly interpolates <paramref name="color"/> toward <paramref name="target"/>.</summary>
    private static Color Blend(Color color, Color target, float amount) => new(
        color.Red + ((target.Red - color.Red) * amount),
        color.Green + ((target.Green - color.Green) * amount),
        color.Blue + ((target.Blue - color.Blue) * amount));
}
