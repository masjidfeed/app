using MasjidApp.Core.Announcements;
using MasjidApp.Core.Caching;
using MasjidApp.Core.Events;
using MasjidApp.Core.Http;
using MasjidApp.Core.Notifications;
using MasjidApp.Core.Observability;
using MasjidApp.Core.Parking;
using MasjidApp.Core.PrayerTimes;
using MasjidApp.Core.Posts;
using MasjidApp.Core.SalahApi;
using MasjidApp.Core.Services;
using MasjidApp.Core.Widgets;
using MasjidApp.Controls;
using MasjidApp.Notifications;
using MasjidApp.Observability;
using MasjidApp.PlatformPermissions;
using MasjidApp.Push;
using MasjidApp.Services;
using MasjidApp.ViewModels;
using MasjidApp.Views;
using MasjidApp.Widgets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls.Hosting;
#if ANDROID
using Microsoft.Maui.Handlers;
#endif
using Microsoft.Maui.Storage;
#if MAUI_DEVFLOW
using Microsoft.Maui.DevFlow.Agent;
#endif
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Plugin.LocalNotification;
using ZXing.Net.Maui.Controls;

namespace MasjidApp;

public static class MauiProgram
{
    private const string UserAgentPlatform =
#if IOS
    "iOS";
#elif ANDROID
    "Android";
#elif MACCATALYST
    "macOS";
#else
    "Unknown";
#endif

    public static MauiApp CreateMauiApp()
    {
#if IOS
        // CVE-2025-6965: the patched SQLite (SQLitePCLRaw 3.0 / SourceGear.sqlite3) ships on iOS
        // only as a static library (e_sqlite3.a), statically linked into the app. Reaching it
        // requires the "__Internal" provider; SQLite-net's lazy initializer would otherwise pick
        // the dynamic-load "e_sqlite3" provider and fail with "DllNotFoundException: e_sqlite3".
        // Pin and freeze the internal provider before any SQLiteConnection is created.
        SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_internal());
        SQLitePCL.raw.FreezeProvider();
#endif
        var builder = MauiApp.CreateBuilder();
#if ANDROID
        WebViewHandler.Mapper.AppendToMapping(
            nameof(DonationWebView),
            static (handler, view) =>
            {
                if (view is DonationWebView or RamadanWebView)
                {
                    handler.PlatformView.Settings.SetSupportMultipleWindows(false);
                }
            });
        // Parking map: zooming uses the native Google Maps zoom buttons and compass
        // (plus pinch gestures) instead of custom app buttons (API-06 parking).
        Microsoft.Maui.Maps.Handlers.MapHandler.Mapper.AppendToMapping(
            "NativeMapControls",
            static (handler, view) =>
            {
                if (view is Microsoft.Maui.Controls.Maps.Map &&
                    handler.PlatformView is Android.Gms.Maps.GoogleMap googleMap)
                {
                    googleMap.UiSettings.ZoomControlsEnabled = true;
                    googleMap.UiSettings.CompassEnabled = true;
                }
            });
#endif
#if MAUI_DEVFLOW
        builder.AddMauiDevFlowAgent();
#endif
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("Outfit-Variable.ttf", "Outfit");
                fonts.AddFont("GildaDisplay-Regular.ttf", "GildaDisplay");
            })
            .UseMauiMaps()
            // C1: register Plugin.LocalNotification and its IMauiInitializeService (FR-PN-07).
            .UseLocalNotification(static notifications =>
            {
#if IOS
                notifications.AddiOS(static ios =>
                    ios.SetCustomUserNotificationCenterDelegate(
                        Platforms.iOS.UnifiedNotificationCenterDelegate.Instance));
#endif
            })
            .UseBarcodeReader();

        RegisterServices(builder.Services);
        ConfigureObservability(builder);

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    // ── Batch E2: OpenTelemetry (traces + metrics + logs) ────────────────────────
    // Core domain logic instruments via System.Diagnostics (ActivitySource/Meter) and
    // ILogger; here the app composes the OpenTelemetry SDK around those primitives.
    // Telemetry is only EXPORTED off-device when the user has opted in (NFR-OB-01).
    // The OTLP endpoint is read from the standard OTEL_EXPORTER_OTLP_ENDPOINT
    // environment variable, so it is configurable without a code change (DV-03).
    private static void ConfigureObservability(MauiAppBuilder builder)
    {
        var consentGiven = Preferences.Default.Get("telemetry_consent", false);
        var serviceVersion = AppInfo.Current.VersionString;

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName: MasjidTelemetry.Name,
                serviceVersion: serviceVersion))
            .WithTracing(tracing =>
            {
                tracing.AddSource(MasjidTelemetry.Name);
                if (consentGiven)
                {
                    tracing.AddOtlpExporter();
                }
            })
            .WithMetrics(metrics =>
            {
                metrics.AddMeter(MasjidTelemetry.Name);
                if (consentGiven)
                {
                    metrics.AddOtlpExporter();
                }
            });

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.SetResourceBuilder(ResourceBuilder.CreateDefault()
                .AddService(serviceName: MasjidTelemetry.Name, serviceVersion: serviceVersion));
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
            if (consentGiven)
            {
                logging.AddOtlpExporter();
            }
        });
    }

    private static void RegisterServices(IServiceCollection services)
    {
        // Cross-cutting services - registered by interface and constructor-injected.
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IUserPreferences, UserPreferences>();
        services.AddSingleton<IDeepLinkRouter, DeepLinkRouter>();
        services.AddSingleton<IPostActionService, PostActionService>();

        // ── Batch E2: Telemetry (opt-in, NFR-OB-01) ─────────────────────────────
        services.AddSingleton<TelemetryService>();

        // ── Batch 01: API Client & Caching ──────────────────────────────────────

        // ICacheStore - SQLite-backed, stored in the app's private data directory.
        // API-08, NFR-OF-03: persists across restarts.
        services.AddSingleton(static _ =>
            new SqliteCacheStore(
                Path.Combine(FileSystem.AppDataDirectory, "masjidapp_cache.db")));
        services.AddSingleton<ICacheStore>(static sp =>
            sp.GetRequiredService<SqliteCacheStore>());
        services.AddSingleton<ICacheStoreLifecycle>(static sp =>
            sp.GetRequiredService<SqliteCacheStore>());

        // IHttpClientFactory - explicit transport timeouts prevent a stalled socket from
        // blocking the UI beyond the resilience pipeline's per-attempt budget (API-11).
        services.AddHttpClient("masjid", static client =>
            ConfigureServerHttpClient(client, TimeSpan.FromSeconds(15)));
        services.AddHttpClient("csv", static client =>
            ConfigureServerHttpClient(client, TimeSpan.FromSeconds(10)));
        services.AddHttpClient("push", static client =>
            ConfigureServerHttpClient(client, TimeSpan.FromSeconds(15)));

        // IMasjidApiClient - typed client built on top of HttpClient + ICacheStore.
        // TC-06: all request URLs are absolute (from the active masjid's stored entry
        // or its config document), so no environment base URL is required here.
        services.AddSingleton<IMasjidApiClient>(static sp =>
            new MasjidApiClient(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("masjid"),
                sp.GetRequiredService<ICacheStore>(),
                pipeline: null));   // uses built-in exponential-backoff + timeout pipeline

        // ── Batch 02: Config Bootstrap, Multi-Masjid ────────────────────────────

        // IMasjidRegistry - persisted list of added masjids (API-10).
        services.AddSingleton<IMasjidRegistry>(static _ => new MasjidRegistry(Preferences.Default));

        // IMasjidContext - fetches active masjid's config document (API-06).
        services.AddSingleton<IMasjidContext, MasjidContext>();

        // ── ViewModels ──────────────────────────────────────────────────────────

        // Batch F1: Home shell ViewModel (singleton - lives as long as the Home tab).
        services.AddSingleton<HomeShellViewModel>();
        services.AddTransient<MasjidSelectionViewModel>();
        services.AddTransient<AddMasjidViewModel>();
        services.AddTransient<SettingsViewModel>();

        // ── Batch A4: Prayer Times Provider & Domain Services ──────────────────
        // ISalahApiService - fetches & caches the SalahAPI 1.1 document (API-01–API-03).
        services.AddSingleton<ISalahApiService, SalahApiService>();

        // IPrayerTimeCalculator - on-device athan/iqama calculation (A2).
        services.AddSingleton<IPrayerTimeCalculator, PrayerTimeCalculator>();

        // ICsvPrayerTimesService - CSV-backed daily athan overrides (A3).
        // Rows persist per day in the shared cache database so the schedule renders offline
        // and the feed is only re-checked once a day (API-03, NFR-OF-03).
        services.AddSingleton(static _ =>
            new SqliteCsvPrayerTimesStore(
                Path.Combine(FileSystem.AppDataDirectory, "masjidapp_cache.db")));
        services.AddSingleton<ICsvPrayerTimesStore>(static sp =>
            sp.GetRequiredService<SqliteCsvPrayerTimesStore>());
        services.AddSingleton<ICacheStoreLifecycle>(static sp =>
            sp.GetRequiredService<SqliteCsvPrayerTimesStore>());
        services.AddSingleton<ICsvPrayerTimesService>(static sp =>
            new CsvPrayerTimesService(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient("csv"),
                sp.GetRequiredService<ICsvPrayerTimesStore>(),
                TimeProvider.System,
                sp.GetRequiredService<ILogger<CsvPrayerTimesService>>()));

        // IPrayerTimesProvider - aggregates SalahAPI, calculation, and CSV (A4).
        services.AddSingleton<IPrayerTimesProvider, PrayerTimesProvider>();

        // ── Batch C1: Local Prayer & Iqama Reminders (FR-PT-10, FR-PN-07) ─────────
        // INotificationService is registered as a singleton by UseLocalNotification().
        services.AddSingleton<IPrayerReminderScheduler, PrayerReminderScheduler>();
        services.AddSingleton<IPrayerReminderSettingsService, PrayerReminderSettingsService>();
        services.AddSingleton<INotificationInboxService, NotificationInboxService>();
        services.AddSingleton<IDeliveredNotificationService, DeliveredNotificationService>();
        services.AddSingleton<IPushNotificationCoordinator, PushNotificationCoordinator>();
        services.AddSingleton<IPushNotificationSettingsService, PushNotificationSettingsService>();
        services.AddSingleton<IDeviceNotificationSettings, DeviceNotificationSettings>();
        services.AddSingleton<IAlertService, AlertService>();

        // ── Batch B2: Events ─────────────────────────────────────────────────────
        services.AddSingleton<IEventsService, EventsService>();
        services.AddSingleton<IPostDetailsService, PostDetailsService>();
        services.AddSingleton<IEventCalendarService, EventCalendarService>();

        // ── Batch B3: Announcements ─────────────────────────────────────────────
        services.AddSingleton<IAnnouncementsService, AnnouncementsService>();

        // ── Unified post detail, notifications, and image preview ────────────────
        services.AddTransient<PostDetailViewModel>();
        services.AddTransient<NotificationsViewModel>();
        services.AddTransient<ImagePreviewViewModel>();

        // ── Batches B4 / B5 / B6 / B8: Contact, Qibla, Donations, About ────────
        // ILocationService - coarse-only on Android (bypasses MAUI's fine-location check);
        // falls back to MAUI Geolocation on iOS (FR-CM-03, NFR-PR-01).
#if ANDROID
        services.AddSingleton<ILocationService, MasjidApp.Platforms.Android.AndroidLocationService>();
#else
        services.AddSingleton<ILocationService, MauiLocationService>();
#endif
        services.AddTransient<ContactViewModel>();
        services.AddSingleton<IMapLocationResolver, MapLocationResolver>();
        services.AddSingleton<IContactActionService, ContactActionService>();
        services.AddSingleton<IAppIconService, AppIconService>();
        services.AddTransient<QiblaViewModel>();
        services.AddSingleton<AboutViewModel>();
        services.AddTransient<DonationsViewModel>();
        // Parking guide (API-06 parking feature flag): map document service plus
        // the pushed parking guide page's ViewModel and view.
        services.AddSingleton<IParkingConfigService, ParkingConfigService>();
        services.AddTransient<ParkingViewModel>();
        // Ramadan home category (API-06 ramadan feature flag): one instance shared
        // with the Home shell so the strip and inline webpage stay in sync.
        services.AddSingleton<RamadanViewModel>();

        // ── Batch A4 ──────────────────────────────────────────────────────────
        services.AddTransient<PrayerTimesViewModel>();
        services.AddTransient<AppearanceViewModel>();
        services.AddTransient<PrayerReminderLeadTimeViewModel>();

        // Shell + Views (resolved through DI by the Shell when navigating).
        services.AddSingleton<AppShell>();
        // Welcome/onboarding: one-time feature showcase before masjid selection.
        services.AddTransient<WelcomeViewModel>();
        services.AddTransient<WelcomePage>();
        // F2: First-run setup page - shown as window root when no masjid is configured.
        services.AddTransient<SetupPage>();
        // Batch F1: Home shell page (singleton - persists across tab switches).
        services.AddSingleton<HomeShellPage>();
        services.AddTransient<MasjidSelectionPage>();
        services.AddTransient<AddMasjidPage>();
        services.AddTransient<QrScannerPage>();
        services.AddSingleton<IQrScannerService, QrScannerService>();
        services.AddTransient<PrivacyPolicyPage>();
        services.AddTransient<PostDetailPage>();
        services.AddTransient<NotificationsPage>();
        services.AddTransient<ImagePreviewPage>();
        services.AddTransient<ContactPage>();
        services.AddTransient<QiblaPage>();
        services.AddTransient<DonationsPage>();
        services.AddTransient<ParkingPage>();
        services.AddTransient<SettingsPage>();
        services.AddTransient<AppearancePage>();
        services.AddTransient<PrayerReminderLeadTimePage>();
        services.AddTransient<BrandedSplashPage>();
        services.AddTransient<PrayerTimesPage>();

        // ── Batch D1: Home-Screen Widgets (TC-04, FR-WG-01–FR-WG-05) ────────────
        // IWidgetDataWriter - writes render-ready snapshot to the shared container.
        services.AddSingleton<IWidgetDataWriter, WidgetDataWriter>();
        // WidgetRefreshService - builds + pushes the snapshot on prayer-data refresh.
        services.AddSingleton<WidgetRefreshService>();
        // IBackgroundRefreshScheduler - periodic OS background regeneration (FR-WG-04).
        services.AddSingleton<IBackgroundRefreshScheduler, BackgroundRefreshScheduler>();
    }

    private static void ConfigureServerHttpClient(HttpClient client, TimeSpan timeout)
    {
        client.Timeout = timeout;
        client.DefaultRequestHeaders.UserAgent.Add(
            MasjidUserAgent.Create(UserAgentPlatform, AppInfo.Current.VersionString));
    }
}
