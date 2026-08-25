using MasjidApp.Core.Caching;
using MasjidApp.Core.Configuration;
using MasjidApp.Core.Services;
using MasjidApp.Push;
using MasjidApp.Services;
using MasjidApp.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
#if IOS
using UIKit;
#endif

namespace MasjidApp;

public partial class App : Application
{
    private readonly IServiceProvider _services;
    private readonly IMasjidContext _masjidContext;
    private readonly IAppIconService _appIconService;
    private readonly IDeepLinkRouter _deepLinkRouter;
    private readonly IPushNotificationSettingsService _pushNotificationSettings;
    private readonly IReadOnlyList<ICacheStoreLifecycle> _cacheStoreLifecycles;
    private readonly ILogger<App> _logger;
    private Task _cacheCloseTask = Task.CompletedTask;

    public App(
        IServiceProvider services,
        IThemeService themeService,
        IMasjidContext masjidContext,
        IAppIconService appIconService,
        IDeepLinkRouter deepLinkRouter,
        IPushNotificationSettingsService pushNotificationSettings,
        IEnumerable<ICacheStoreLifecycle> cacheStoreLifecycles,
        ILogger<App> logger)
    {
        InitializeComponent();
        _services = services;
        _masjidContext = masjidContext;
        _appIconService = appIconService;
        _deepLinkRouter = deepLinkRouter;
        _pushNotificationSettings = pushNotificationSettings;
        _cacheStoreLifecycles = cacheStoreLifecycles.ToList();
        _logger = logger;

        // Apply the persisted light/dark/system theme before the first window shows (FR-ST-03).
        themeService.Initialize();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // F2: show the mandatory first-run setup screen when no masjid is configured;
        // go straight to the normal shell when one is already active (API-10, FR-ST-01).
        // FR-ST-08: native platform launch screens remain plain white until MAUI starts.
        var activeEntry = _masjidContext.ActiveEntry;
        var branding = TenantBranding.Get(activeEntry);
        _ = _appIconService.ApplyAsync(branding);

        Page rootPage = activeEntry is null
            ? _services.GetRequiredService<SetupPage>()
            : _services.GetRequiredService<BrandedSplashPage>();

        var window = new Window(rootPage);
        window.Stopped += OnWindowStopped;
        window.Resumed += OnWindowResumed;
        _ = _pushNotificationSettings.RefreshAsync();
        return window;
    }

    /// <inheritdoc />
    protected override void OnAppLinkRequestReceived(Uri uri)
    {
        base.OnAppLinkRequestReceived(uri);
        _ = _deepLinkRouter.RouteAsync(uri);
    }

    private void OnWindowStopped(object? sender, EventArgs e)
    {
        if (sender is Window { Page: Shell shell } &&
            shell.CurrentPage is IAppLifecycleAware lifecycleAwarePage)
        {
            lifecycleAwarePage.OnAppStopped();
        }

        _cacheCloseTask = CloseCacheForSuspensionAsync();
    }

    private async void OnWindowResumed(object? sender, EventArgs e)
    {
        await _cacheCloseTask;

        if (sender is Window { Page: Shell shell } &&
            shell.CurrentPage is IAppLifecycleAware lifecycleAwarePage)
        {
            lifecycleAwarePage.OnAppResumed();
        }
    }

    private async Task CloseCacheForSuspensionAsync()
    {
#if IOS
        var application = UIApplication.SharedApplication;
        using var expirationCts = new CancellationTokenSource();
        var backgroundTaskId = UIApplication.BackgroundTaskInvalid;

        void EndBackgroundTask()
        {
            var taskId = Interlocked.Exchange(
                ref backgroundTaskId,
                UIApplication.BackgroundTaskInvalid);
            if (taskId != UIApplication.BackgroundTaskInvalid)
            {
                application.EndBackgroundTask(taskId);
            }
        }

        backgroundTaskId = application.BeginBackgroundTask(
            "Close SQLite cache",
            () =>
            {
                expirationCts.Cancel();
                EndBackgroundTask();
            });
#endif

        try
        {
            foreach (var lifecycle in _cacheStoreLifecycles)
            {
#if IOS
                await lifecycle.CloseAsync(expirationCts.Token);
#else
                await lifecycle.CloseAsync();
#endif
            }
        }
        catch (OperationCanceledException)
        {
            LogCacheCloseCanceled(_logger);
        }
        catch (Exception ex)
        {
            LogCacheCloseFailed(_logger, ex);
        }
        finally
        {
#if IOS
        EndBackgroundTask();
#endif
        }
    }

    [LoggerMessage(EventId = 1001, Level = LogLevel.Warning, Message = "Cache closure expired while the app was entering the background.")]
    private static partial void LogCacheCloseCanceled(ILogger logger);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Error, Message = "Failed to close the cache while the app was entering the background.")]
    private static partial void LogCacheCloseFailed(ILogger logger, Exception exception);
}
