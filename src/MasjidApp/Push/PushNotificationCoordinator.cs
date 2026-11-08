using System.Net.Http.Json;
using System.Text.Json;
using MasjidApp.Core.Models;
using MasjidApp.Core.Http;
using MasjidApp.Core.Posts;
using MasjidApp.Core.Push;
using MasjidApp.Core.Services;
using MasjidApp.Core.Utils;
using MasjidApp.Notifications;
using MasjidApp.Services;
using Microsoft.Extensions.Logging;
#if IOS || ANDROID
using Plugin.Firebase.AppCheck;
using Plugin.Firebase.CloudMessaging;
using Plugin.Firebase.CloudMessaging.EventArgs;
#endif
#if ANDROID
using Firebase;
using Plugin.Firebase.Core.Platforms.Android;
#elif IOS
using Firebase.Core;
using Plugin.Firebase.Core.Platforms.iOS;
#endif

namespace MasjidApp.Push;

/// <inheritdoc />
public sealed partial class PushNotificationCoordinator : IPushNotificationCoordinator, IDisposable
{
    private const string RegistrationStateKey = "push_registration_state";
    private const string LastPresentedNotificationIdKey = "push_last_presented_notification_id";
    private const string LastHandledNotificationIdKey = "push_last_handled_notification_id";
    private const string PlatformName =
#if IOS
        "ios";
#elif ANDROID
        "android";
#else
        "unsupported";
#endif

    private readonly IMasjidContext _context;
    private readonly IDeepLinkRouter _deepLinkRouter;
    private readonly IUserPreferences _preferences;
    private readonly INotificationInboxService _inbox;
    private readonly IPostDetailsService _postDetailsService;
    private readonly HttpClient _http;
    private readonly ILogger<PushNotificationCoordinator> _logger;
    private readonly SemaphoreSlim _syncLock = new(1, 1);
#if IOS || ANDROID
    private IFirebaseCloudMessaging? _messaging;
    private string? _initializedAppId;
#endif

    public PushNotificationCoordinator(
        IMasjidContext context,
        IDeepLinkRouter deepLinkRouter,
        IUserPreferences preferences,
        INotificationInboxService inbox,
        IPostDetailsService postDetailsService,
        IHttpClientFactory httpClientFactory,
        ILogger<PushNotificationCoordinator> logger)
    {
        _context = context;
        _deepLinkRouter = deepLinkRouter;
        _preferences = preferences;
        _inbox = inbox;
        _postDetailsService = postDetailsService;
        _http = httpClientFactory.CreateClient("push");
        _logger = logger;
        _context.ActiveMasjidChanged += OnActiveMasjidChanged;
    }

    /// <inheritdoc />
    public async Task<bool> SyncAsync(
        bool enabled,
        bool forceReRegister = false,
        CancellationToken ct = default)
    {
#if !(IOS || ANDROID)
        await Task.CompletedTask;
        return true;
#else
        await _syncLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var saved = await LoadStateAsync().ConfigureAwait(false);
            var configResult = await _context.GetConfigAsync(ct: ct).ConfigureAwait(false);
            var config = configResult?.ValueOrDefault;
            var push = config?.PushNotifications;
            var masjidId = config?.Masjid?.Id;
            var canRegister = enabled && push is { Enabled: true, Configured: true } &&
                !string.IsNullOrWhiteSpace(masjidId) &&
                Uri.TryCreate(push.RegistrationUrl, UriKind.Absolute, out _);

            if (saved is not null &&
                (!canRegister || !string.Equals(saved.MasjidId, masjidId, StringComparison.Ordinal) ||
                 !string.Equals(saved.RegistrationUrl, push?.RegistrationUrl, StringComparison.Ordinal) ||
                 !SameFirebaseApp(saved.Firebase, push?.Firebase)))
            {
                if (!await UnregisterAsync(saved, ct).ConfigureAwait(false))
                {
                    return false;
                }

                SecureStorage.Default.Remove(RegistrationStateKey);
                saved = null;
                await ResetFirebaseAsync().ConfigureAwait(false);
            }

            if (!canRegister || push?.Firebase is null)
            {
                return true;
            }

            await EnsureFirebaseAsync(push.Firebase).ConfigureAwait(false);
            await _messaging!.CheckIfValidAsync().ConfigureAwait(false);
            var token = await _messaging.GetTokenAsync().ConfigureAwait(false);
            return await RegisterAsync(
                masjidId!,
                push.RegistrationUrl!,
                push.Firebase,
                token,
                forceReRegister ? null : saved?.Token,
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogPushSyncFailed(_logger, ex);
            return false;
        }
        finally
        {
            _syncLock.Release();
        }
#endif
    }

    /// <inheritdoc />
    public async Task HandleNotificationTapAsync(
        string? title,
        string? body,
        string? imageUrl,
        IDictionary<string, string> data,
        CancellationToken ct = default)
    {
        StoreNotification(title, body, imageUrl, data);
        if (!data.TryGetValue("deepLink", out var deepLink) ||
            !Uri.TryCreate(deepLink, UriKind.Absolute, out var uri))
        {
            return;
        }

        if (data.TryGetValue("notificationId", out var notificationId))
        {
            var previousId = Preferences.Default.Get(LastHandledNotificationIdKey, string.Empty);
            if (string.Equals(previousId, notificationId, StringComparison.Ordinal))
            {
                await HydrateNotificationAsync(title, body, imageUrl, data, ct)
                    .ConfigureAwait(false);
                MarkNotificationRead(data, notificationId);
                return;
            }
        }

        try
        {
            if (await _deepLinkRouter.RouteAsync(uri, ct).ConfigureAwait(false) &&
                data.TryGetValue("notificationId", out var handledNotificationId))
            {
                await HydrateNotificationAsync(title, body, imageUrl, data, ct)
                    .ConfigureAwait(false);
                MarkNotificationRead(data, handledNotificationId);
                Preferences.Default.Set(LastHandledNotificationIdKey, handledNotificationId);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogPushTapFailed(_logger, ex);
        }
    }

    private async Task HydrateNotificationAsync(
        string? title,
        string? body,
        string? imageUrl,
        IDictionary<string, string> data,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(body) ||
            !data.TryGetValue("deepLink", out var deepLinkValue) ||
            !Uri.TryCreate(deepLinkValue, UriKind.Absolute, out var uri) ||
            !PostDeepLink.TryParse(uri, out var deepLink) ||
            deepLink is null ||
            !data.TryGetValue("notificationId", out var notificationId) ||
            !data.TryGetValue("masjidId", out var masjidId))
        {
            return;
        }

        var config = (await _context.GetConfigAsync(ct: ct).ConfigureAwait(false))?.ValueOrDefault;
        if (config is null ||
            !string.Equals(config.Masjid?.Id, masjidId, StringComparison.Ordinal))
        {
            return;
        }

        var feedUrl = deepLink.Type == PostDeepLinkType.Events
            ? config.Endpoints?.EventsUrl
            : config.Endpoints?.AnnouncementsUrl;
        if (string.IsNullOrWhiteSpace(feedUrl))
        {
            return;
        }

        var post = (await _postDetailsService.GetPostAsync(
            feedUrl,
            deepLink.PostId,
            ApiCachePolicy.PreferCache,
            ct).ConfigureAwait(false)).ValueOrDefault;
        if (post is null)
        {
            return;
        }

        var summary = HtmlSanitizer.ToPlainText(post.Description);
        if (summary.Length > 180)
        {
            summary = $"{summary[..177]}...";
        }

        data.TryGetValue("type", out var type);
        _inbox.Upsert(new NotificationInboxItem(
            notificationId,
            masjidId,
            string.IsNullOrWhiteSpace(title) ? post.Title : title,
            string.IsNullOrWhiteSpace(body) ? summary : body,
            imageUrl ?? post.ImageUrl,
            deepLinkValue,
            type,
            post.PostId,
            DateTimeOffset.UtcNow,
            IsRead: false));
    }

    private void MarkNotificationRead(
        IDictionary<string, string> data,
        string notificationId)
    {
        if (data.TryGetValue("masjidId", out var masjidId) &&
            !string.IsNullOrWhiteSpace(masjidId))
        {
            _inbox.MarkRead(masjidId, notificationId);
        }
    }

    private void StoreNotification(
        string? title,
        string? body,
        string? imageUrl,
        IDictionary<string, string> data)
    {
        if (!data.TryGetValue("notificationId", out var notificationId) ||
            !data.TryGetValue("masjidId", out var masjidId) ||
            string.IsNullOrWhiteSpace(notificationId) ||
            string.IsNullOrWhiteSpace(masjidId))
        {
            return;
        }

        int? postId = data.TryGetValue("postId", out var postIdValue) &&
            int.TryParse(postIdValue, out var parsedPostId)
                ? parsedPostId
                : null;
        data.TryGetValue("deepLink", out var deepLink);
        data.TryGetValue("type", out var type);
        _inbox.Upsert(new NotificationInboxItem(
            notificationId,
            masjidId,
            title ?? string.Empty,
            body ?? string.Empty,
            imageUrl,
            deepLink,
            type,
            postId,
            DateTimeOffset.UtcNow,
            IsRead: false));
    }

#if IOS || ANDROID
    private async Task EnsureFirebaseAsync(FirebaseConfig firebase)
    {
        var appId = GetPlatformAppId(firebase);
        if (string.Equals(_initializedAppId, appId, StringComparison.Ordinal))
        {
            return;
        }

        if (_initializedAppId is not null)
        {
            await ResetFirebaseAsync().ConfigureAwait(false);
        }

#if DEBUG
        CrossFirebaseAppCheck.Configure(AppCheckOptions.Debug);
#elif IOS
        CrossFirebaseAppCheck.Configure(AppCheckOptions.AppAttest);
#else
        CrossFirebaseAppCheck.Configure(AppCheckOptions.PlayIntegrity);
#endif

#if ANDROID
        var platform = firebase.Android ?? throw new InvalidOperationException("Android Firebase configuration is missing.");
        var activity = Platform.CurrentActivity ?? throw new InvalidOperationException("No current Android activity is available.");
        var options = new FirebaseOptions.Builder()
            .SetApplicationId(Required(platform.AppId, "appId"))
            .SetGcmSenderId(Required(platform.SenderId, "senderId"))
            .SetApiKey(Required(platform.ApiKey, "apiKey"))
            .Build();
        CrossFirebase.Initialize(activity, () => Platform.CurrentActivity!, options);
#elif IOS
        var platform = firebase.Ios ?? throw new InvalidOperationException("iOS Firebase configuration is missing.");
        var options = new Options(
            Required(platform.AppId, "appId"),
            Required(platform.SenderId, "senderId"))
        {
            ApiKey = Required(platform.ApiKey, "apiKey"),
            BundleId = AppInfo.PackageName,
        };
        CrossFirebase.Initialize(firebaseOptions: options);
        FirebaseCloudMessagingImplementation.Initialize();
        Platforms.iOS.UnifiedNotificationCenterDelegate.Install();
#endif

        _messaging = CrossFirebaseCloudMessaging.Current;
        _messaging.TokenChanged += OnTokenChanged;
        _messaging.NotificationReceived += OnNotificationReceived;
        _messaging.NotificationTapped += OnNotificationTapped;
        _initializedAppId = appId;
    }

    private async Task ResetFirebaseAsync()
    {
        if (_messaging is not null)
        {
            _messaging.TokenChanged -= OnTokenChanged;
            _messaging.NotificationReceived -= OnNotificationReceived;
            _messaging.NotificationTapped -= OnNotificationTapped;
            _messaging = null;
        }

#if ANDROID
        if (Plugin.Firebase.Core.Platforms.Android.CrossFirebase.TryGetDefaultApp(out var app))
        {
            app.Delete();
        }
#elif IOS
        if (Firebase.Core.App.DefaultInstance is { } app)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            app.Delete(success =>
            {
                if (success)
                {
                    completion.TrySetResult();
                }
                else
                {
                    completion.TrySetException(new InvalidOperationException("Firebase iOS app deletion failed."));
                }
            });
            await completion.Task.ConfigureAwait(false);
        }
#endif
        _initializedAppId = null;
    }

    private async Task<bool> RegisterAsync(
        string masjidId,
        string registrationUrl,
        FirebaseConfig firebase,
        string token,
        string? previousToken,
        CancellationToken ct)
    {
        if (string.Equals(token, previousToken, StringComparison.Ordinal))
        {
            return true;
        }

        try
        {
            var request = new PushRegistrationRequest(masjidId, PlatformName, token, previousToken);
            using var message = new HttpRequestMessage(HttpMethod.Post, registrationUrl)
            {
                Content = JsonContent.Create(request, PushJsonContext.Default.PushRegistrationRequest),
            };
            FirebaseAppCheckHeader.Add(
                message,
                await CrossFirebaseAppCheck.GetTokenAsync().ConfigureAwait(false));
            using var response = await _http.SendAsync(message, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await SaveStateAsync(new PushRegistrationState(masjidId, registrationUrl, token, firebase))
                .ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogPushRegisterFailed(_logger, ex);
            return false;
        }
    }

    private async Task<bool> UnregisterAsync(PushRegistrationState state, CancellationToken ct)
    {
        try
        {
            await EnsureFirebaseAsync(state.Firebase).ConfigureAwait(false);
            var request = new PushRegistrationRequest(state.MasjidId, PlatformName, state.Token);
            using var message = new HttpRequestMessage(HttpMethod.Delete, state.RegistrationUrl)
            {
                Content = JsonContent.Create(request, PushJsonContext.Default.PushRegistrationRequest),
            };
            FirebaseAppCheckHeader.Add(
                message,
                await CrossFirebaseAppCheck.GetTokenAsync().ConfigureAwait(false));
            using var response = await _http.SendAsync(message, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogPushUnregisterFailed(_logger, ex);
            return false;
        }
    }

    private void OnTokenChanged(object? sender, FCMTokenChangedEventArgs e) =>
        _ = SyncAsync(enabled: true);

    private void OnNotificationReceived(object? sender, FCMNotificationReceivedEventArgs e)
    {
        if (e.Notification.Data is { } notificationData)
        {
            StoreNotification(
                e.Notification.Title,
                e.Notification.Body,
                e.Notification.ImageUrl,
                notificationData);
        }

        var data = e.Notification.Data;
        if (data is null || !data.TryGetValue("notificationId", out var notificationId))
        {
            return;
        }

        var previousId = Preferences.Default.Get(LastPresentedNotificationIdKey, string.Empty);
        if (string.Equals(previousId, notificationId, StringComparison.Ordinal))
        {
            data["is_silent_in_foreground"] = "true";
            return;
        }

        Preferences.Default.Set(LastPresentedNotificationIdKey, notificationId);
    }

    private async void OnNotificationTapped(object? sender, FCMNotificationTappedEventArgs e)
    {
        if (e.Notification.Data is not { } data)
        {
            return;
        }

        await HandleNotificationTapAsync(
            e.Notification.Title,
            e.Notification.Body,
            e.Notification.ImageUrl,
            data).ConfigureAwait(false);
    }

    private static string GetPlatformAppId(FirebaseConfig firebase) =>
#if IOS
        Required(firebase.Ios?.AppId, "ios.appId");
#else
        Required(firebase.Android?.AppId, "android.appId");
#endif

    private static bool SameFirebaseApp(FirebaseConfig left, FirebaseConfig? right) =>
        right is not null && string.Equals(GetPlatformAppId(left), GetPlatformAppId(right), StringComparison.Ordinal);

    private static string Required(string? value, string field) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Firebase {field} is missing.")
            : value;

    private static async Task<PushRegistrationState?> LoadStateAsync()
    {
        var json = await SecureStorage.Default.GetAsync(RegistrationStateKey).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize(json, PushJsonContext.Default.PushRegistrationState);
    }

    private static Task SaveStateAsync(PushRegistrationState state) =>
        SecureStorage.Default.SetAsync(
            RegistrationStateKey,
            JsonSerializer.Serialize(state, PushJsonContext.Default.PushRegistrationState));
#endif

    private void OnActiveMasjidChanged(object? sender, EventArgs e) =>
        _ = SyncAsync(
            _preferences.GetValue(
                PushNotificationSettingsService.StateKey,
                (int)PushNotificationState.Disabled) != (int)PushNotificationState.Disabled);

    public void Dispose()
    {
        _context.ActiveMasjidChanged -= OnActiveMasjidChanged;
#if IOS || ANDROID
        if (_messaging is not null)
        {
            _messaging.TokenChanged -= OnTokenChanged;
            _messaging.NotificationReceived -= OnNotificationReceived;
            _messaging.NotificationTapped -= OnNotificationTapped;
        }
#endif
        _syncLock.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Remote push synchronization failed.")]
    private static partial void LogPushSyncFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Remote push registration failed.")]
    private static partial void LogPushRegisterFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Remote push could not be unregistered from the previous tenant.")]
    private static partial void LogPushUnregisterFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Remote notification deep-link navigation failed.")]
    private static partial void LogPushTapFailed(ILogger logger, Exception exception);
}