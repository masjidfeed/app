using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasjidApp.Core.Announcements;
using MasjidApp.Core.Configuration;
using MasjidApp.Core.Events;
using MasjidApp.Core.Http;
using MasjidApp.Core.Models;
using MasjidApp.Core.Observability;
using MasjidApp.Core.PrayerTimes;
using MasjidApp.Core.Posts;
using MasjidApp.Core.Push;
using MasjidApp.Core.Services;
using MasjidApp.Notifications;
using MasjidApp.Services;
using Microsoft.Extensions.Logging;

namespace MasjidApp.ViewModels;

// FR-PT-01, FR-PT-02, FR-CM-01, FR-EV-01, API-06, API-10, TC-03, NFR-AC-01

/// <summary>Swipeable home-shell categories shown in the carousel.</summary>
public enum HomeShellCategory
{
    /// <summary>Announcements and next-prayer summary.</summary>
    Announcements,

    /// <summary>Upcoming event preview.</summary>
    Events,

    /// <summary>Masjid contact and about information.</summary>
    About,
}

/// <summary>A swipeable home-shell page and the shell ViewModel it renders.</summary>
/// <param name="Category">The category represented by the page.</param>
/// <param name="Shell">The home shell ViewModel that owns the page data.</param>
public sealed record HomeShellCategoryPage(HomeShellCategory Category, HomeShellViewModel Shell);

/// <summary>
/// ViewModel for the Home shell page - masjid logo, collapsing header,
/// horizontal category strip ("Today", "Events", "Qibla"), and
/// swappable feed content below the strip.
/// </summary>
public sealed partial class HomeShellViewModel : ViewModelBase
{
    private const string TimeFormatKey = "settings_24h";

    private readonly IPrayerTimesProvider _prayerTimesProvider;
    private readonly IAnnouncementsService _announcementsService;
    private readonly IEventsService _eventsService;
    private readonly IPostDetailsService _postDetailsService;
    private readonly IMasjidContext _context;
    private readonly INavigationService _navigation;
    private readonly MasjidApp.Widgets.WidgetRefreshService _widgetRefresh;
    private readonly IPrayerReminderSettingsService _prayerReminderSettings;
    private readonly IUserPreferences _preferences;
    private readonly INotificationInboxService _notificationInbox;
    private readonly IDeliveredNotificationService _deliveredNotifications;
    private readonly ILogger<HomeShellViewModel> _logger;
    private IDispatcherTimer? _countdownTimer;
    private IReadOnlyList<DailyPrayerSchedule> _countdownSchedules = [];
    private bool _hasLoadedOnce;

    // ─── Branding (API-06) ──────────────────────────────────────────────────────

    /// <summary>Active masjid's display name shown in the collapsing header.</summary>
    [ObservableProperty]
    private string _masjidName = "Masjid";

    /// <summary>Absolute URL of the masjid logo; <c>null</c> shows the default app icon.</summary>
    [ObservableProperty]
    private string? _logoUrl;

    /// <summary>Whether the active masjid has unread remote notifications.</summary>
    [ObservableProperty]
    private bool _hasUnreadNotifications;

    // ─── Upcoming prayer event (FR-PT-02) ───────────────────────────────────────

    /// <summary>Minute-level summary of the nearest upcoming athan or iqama.</summary>
    [ObservableProperty]
    private string _upcomingPrayerEventText = string.Empty;

    /// <summary>Compact iqama-first prayer columns displayed below the countdown.</summary>
    [ObservableProperty]
    private IReadOnlyList<HomePrayerTimeRow> _homePrayerRows = [];

    // ─── Category strip ─────────────────────────────────────────────────────────

    /// <summary>
    /// Zero-based index of the selected category strip item, indexing into
    /// <see cref="CategoryPages"/> (which shrinks when feature flags disable a category).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTodaySelected))]
    [NotifyPropertyChangedFor(nameof(IsEventsSelected))]
    [NotifyPropertyChangedFor(nameof(IsAboutSelected))]
    private int _selectedCategoryIndex;

    /// <summary>The category currently selected in the strip, or <c>null</c> when out of range.</summary>
    public HomeShellCategory? SelectedCategory =>
        SelectedCategoryIndex >= 0 && SelectedCategoryIndex < CategoryPages.Count
            ? CategoryPages[SelectedCategoryIndex].Category
            : null;

    /// <summary><c>true</c> when the "Today" category is active.</summary>
    public bool IsTodaySelected => SelectedCategory == HomeShellCategory.Announcements;

    /// <summary><c>true</c> when the "Events" category is active.</summary>
    public bool IsEventsSelected => SelectedCategory == HomeShellCategory.Events;

    /// <summary><c>true</c> when the "About" category is active.</summary>
    public bool IsAboutSelected => SelectedCategory == HomeShellCategory.About;

    /// <summary>About sub-ViewModel hosted inline in the "About" category (FR-ST-05, FR-CM-02, FR-CM-04).</summary>
    public AboutViewModel About { get; }

    /// <summary>Swipe pages hosted by the home shell carousel; rebuilt when feature flags load.</summary>
    public ObservableCollection<HomeShellCategoryPage> CategoryPages { get; }

    /// <summary>Whether the announcements feature is enabled for the active masjid (API-06).</summary>
    [ObservableProperty]
    private bool _isAnnouncementsEnabled = true;

    /// <summary>Whether the events feature is enabled for the active masjid (API-06).</summary>
    [ObservableProperty]
    private bool _isEventsEnabled = true;

    private readonly IReadOnlyList<HomeShellCategoryPage> _allCategoryPages;

    // ─── "Today" feed (FR-CM-01) ────────────────────────────────────────────────

    /// <summary>Newest announcements for the Today dashboard.</summary>
    [ObservableProperty]
    private ObservableCollection<MasjidPost> _recentAnnouncements = [];

    // ─── "Events" category (FR-EV-01) ───────────────────────────────────────────

    /// <summary>Upcoming event occurrences shown in the Events category.</summary>
    [ObservableProperty]
    private ObservableCollection<MasjidEventOccurrence> _upcomingEvents = [];

    // ─── "Prayer Times" category (FR-PT-01) ─────────────────────────────────────

    /// <summary>Today's fully-resolved prayer schedule for the Prayer Times category.</summary>
    [ObservableProperty]
    private DailyPrayerSchedule? _todaySchedule;

    // ─── Offline / freshness state (NFR-OF-02) ──────────────────────────────────

    /// <summary>Offline/stale indicator derived from the most recent API results.</summary>
    [ObservableProperty]
    private ContentState _contentState = new(false, null, false);

    /// <summary>Whether a user-initiated pull-to-refresh gesture is active.</summary>
    [ObservableProperty]
    private bool _isRefreshing;

    // ─── Constructor ────────────────────────────────────────────────────────────

    /// <summary>Creates the ViewModel with all required dependencies.</summary>
    public HomeShellViewModel(
        IPrayerTimesProvider prayerTimesProvider,
        IAnnouncementsService announcementsService,
        IEventsService eventsService,
        IPostDetailsService postDetailsService,
        IMasjidContext context,
        INavigationService navigation,
        MasjidApp.Widgets.WidgetRefreshService widgetRefresh,
        IPrayerReminderSettingsService prayerReminderSettings,
        IUserPreferences preferences,
        INotificationInboxService notificationInbox,
        IDeliveredNotificationService deliveredNotifications,
        AboutViewModel about,
        ILogger<HomeShellViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(prayerTimesProvider);
        ArgumentNullException.ThrowIfNull(announcementsService);
        ArgumentNullException.ThrowIfNull(eventsService);
        ArgumentNullException.ThrowIfNull(postDetailsService);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(widgetRefresh);
        ArgumentNullException.ThrowIfNull(prayerReminderSettings);
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(notificationInbox);
        ArgumentNullException.ThrowIfNull(deliveredNotifications);
        ArgumentNullException.ThrowIfNull(about);
        ArgumentNullException.ThrowIfNull(logger);

        _prayerTimesProvider = prayerTimesProvider;
        _announcementsService = announcementsService;
        _eventsService = eventsService;
        _postDetailsService = postDetailsService;
        _context = context;
        _navigation = navigation;
        _widgetRefresh = widgetRefresh;
        _prayerReminderSettings = prayerReminderSettings;
        _preferences = preferences;
        _notificationInbox = notificationInbox;
        _deliveredNotifications = deliveredNotifications;
        _logger = logger;
        About = about;
        _allCategoryPages =
        [
            new(HomeShellCategory.Announcements, this),
            new(HomeShellCategory.Events, this),
            new(HomeShellCategory.About, this),
        ];
        CategoryPages = new ObservableCollection<HomeShellCategoryPage>(_allCategoryPages);
        Title = "Home";
        _notificationInbox.Changed += OnNotificationInboxChanged;
        _context.ActiveMasjidChanged += OnNotificationInboxChanged;
        UpdateUnreadNotifications();
    }

    // ─── Commands ───────────────────────────────────────────────────────────────

    /// <summary>Loads all home-screen data. Bound to page-appearing and pull-to-refresh.</summary>
    [RelayCommand]
    private Task LoadAsync(CancellationToken ct) =>
        LoadDataAsync(ApiCachePolicy.PreferCache, ct);

    /// <summary>Reloads all home-screen data while bypassing the 24-hour cache window.</summary>
    [RelayCommand]
    private Task RefreshAsync(CancellationToken ct) =>
        LoadDataAsync(ApiCachePolicy.ForceRefresh, ct);

    private async Task LoadDataAsync(ApiCachePolicy cachePolicy, CancellationToken ct)
    {
        // Re-appearing with cache-satisfied data must not flash the refresh spinner
        // or rebuild the lists; only a first load or an explicit pull shows progress.
        var showBusy = cachePolicy == ApiCachePolicy.ForceRefresh || !_hasLoadedOnce;
        if (showBusy)
        {
            IsBusy = true;
        }

        try
        {
            await ImportDeliveredNotificationsAsync(ct);

            // ── Branding & endpoints from config (API-06, API-10) ─────────────
            var configResult = await _context.GetConfigAsync(cachePolicy, ct);
            var config = configResult?.ValueOrDefault;
            if (config is not null)
            {
                MasjidName = config.Masjid?.Name ?? "Masjid";
                LogoUrl = EndpointUrlResolver.ResolveAgainstOrigin(
                    config.Branding?.LogoUrl,
                    _context.ActiveEntry?.ConfigUrl);

                // Hide disabled feature categories and clear their feeds (API-06).
                ApplyFeatureFlags(config);
            }

            // FR-CM-01, FR-EV-01, NFR-RL-01: start all content branches together so
            // prayer/widget/reminder latency cannot starve either feed. A disabled
            // feature flag resolves to a null feed URL, which clears the cached list.
            var prayerTask = LoadPrayerDataAsync(cachePolicy, ct);
            var announcementsTask = config is null
                ? Task.FromResult(new ContentState(false, null, false))
                : LoadAnnouncementsSafelyAsync(
                    config.FeatureFlags?.Announcements == true
                        ? config.Endpoints?.AnnouncementsUrl
                        : null,
                    cachePolicy, ct);
            var eventsTask = config is null
                ? Task.FromResult(new ContentState(false, null, false))
                : LoadEventsSafelyAsync(
                    config.FeatureFlags?.Events == true
                        ? config.Endpoints?.EventsUrl
                        : null,
                    cachePolicy, ct);

            await Task.WhenAll(prayerTask, announcementsTask, eventsTask);

            var prayerState = await prayerTask;
            var announcementState = await announcementsTask;
            var eventState = await eventsTask;
            ContentState = configResult is null
                ? ContentState.Combine(prayerState, announcementState, eventState)
                : ContentState.Combine(
                    ContentState.FromApiResult(configResult),
                    prayerState,
                    announcementState,
                    eventState);

            _hasLoadedOnce = true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal cancellation when the page navigates away.
        }
#pragma warning disable CA1031 // NFR-RL-01: surface a degraded state and keep the Home shell usable.
        catch (Exception ex)
        {
            LogHomeLoadFailed(_logger, ex);
            ContentState = ContentState with { IsOffline = true };
            _hasLoadedOnce = true;
        }
#pragma warning restore CA1031
        finally
        {
            if (showBusy)
            {
                IsBusy = false;
            }

            if (cachePolicy == ApiCachePolicy.ForceRefresh)
            {
                IsRefreshing = false;
            }
        }
    }

    private async Task ImportDeliveredNotificationsAsync(CancellationToken ct)
    {
        var delivered = await _deliveredNotifications.GetDeliveredAsync(ct);
        foreach (var notification in delivered)
        {
            _notificationInbox.Upsert(notification);
        }

        UpdateUnreadNotifications();
    }

    private async Task<ContentState> LoadPrayerDataAsync(
        ApiCachePolicy cachePolicy,
        CancellationToken ct)
    {
        ApiResult<DailyPrayerSchedule> result;
        try
        {
            // FR-PT-01, FR-PT-02: GetTodayAsync recalculates "today" in the masjid
            // timezone, including after a calendar-day change while suspended.
            result = await _prayerTimesProvider.GetTodayAsync(cachePolicy, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // NFR-RL-01: isolate prayer/widget/reminder failures from the feeds.
        catch (Exception ex)
        {
            LogPrayerLoadFailed(_logger, ex);
            return new ContentState(true, null, false);
        }
#pragma warning restore CA1031

        if (result.ValueOrDefault is not DailyPrayerSchedule schedule)
        {
            return ContentState.FromApiResult(result);
        }

        TodaySchedule = schedule;
        RebuildHomePrayerRows(schedule);
        _countdownSchedules = [schedule];
        UpdateCountdown();

        _ = UpdatePrayerDependentsAsync(schedule, ct);
        return ContentState.FromApiResult(result);
    }

    private async Task UpdatePrayerDependentsAsync(
        DailyPrayerSchedule schedule,
        CancellationToken ct)
    {
        try
        {
            var from = schedule.Date;
            var range = await _prayerTimesProvider.GetRangeAsync(from, from.AddDays(6), ct: ct);
            _countdownSchedules = range.Count > 0 ? range : [schedule];
            UpdateCountdown();

            await _widgetRefresh.RefreshAsync(ct);
            await _prayerReminderSettings.SyncAsync(_countdownSchedules, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal when the initiating page or app lifecycle stops.
        }
#pragma warning disable CA1031 // NFR-RL-01: auxiliary updates must not degrade successfully loaded prayer content.
        catch (Exception ex)
        {
            LogPrayerDependentUpdatesFailed(_logger, ex);
        }
#pragma warning restore CA1031
    }

    private async Task<ContentState> LoadAnnouncementsSafelyAsync(
        string? announcementsUrl,
        ApiCachePolicy cachePolicy,
        CancellationToken ct)
    {
        try
        {
            return await LoadAnnouncementsAsync(announcementsUrl, cachePolicy, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // NFR-RL-01: one failed feed must not abort the aggregate Home load.
        catch (Exception ex)
        {
            LogAnnouncementsLoadFailed(_logger, ex);
            return new ContentState(true, null, false);
        }
#pragma warning restore CA1031
    }

    private async Task<ContentState> LoadEventsSafelyAsync(
        string? eventsUrl,
        ApiCachePolicy cachePolicy,
        CancellationToken ct)
    {
        try
        {
            return await LoadEventsAsync(eventsUrl, cachePolicy, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // NFR-RL-01: one failed feed must not abort the aggregate Home load.
        catch (Exception ex)
        {
            LogEventsLoadFailed(_logger, ex);
            return new ContentState(true, null, false);
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// Loads the cached announcements feed while the API client revalidates it in the background.
    /// </summary>
    private async Task<ContentState> LoadAnnouncementsAsync(
        string? announcementsUrl,
        ApiCachePolicy cachePolicy,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(announcementsUrl))
        {
            ReplaceIfChanged(RecentAnnouncements, []);
            return new ContentState(false, null, false);
        }

        var result = await _announcementsService
            .GetAnnouncementsAsync(announcementsUrl, cachePolicy, ct);
        var items = FeedRefreshPolicy.ShouldReplaceItems(result) ? result.ValueOrDefault : null;

        if (items is not null)
        {
            ReplaceIfChanged(RecentAnnouncements, items);
        }

        return ContentState.FromApiResult(result);
    }

    /// <summary>
    /// Loads cached events while the API client revalidates them in the background.
    /// </summary>
    private async Task<ContentState> LoadEventsAsync(
        string? eventsUrl,
        ApiCachePolicy cachePolicy,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(eventsUrl))
        {
            ReplaceIfChanged(UpcomingEvents, []);
            return new ContentState(false, null, false);
        }

        var result = await _eventsService.GetEventsAsync(eventsUrl, cachePolicy, ct);
        var occurrences = FeedRefreshPolicy.ShouldReplaceItems(result)
            ? GetUpcoming(result.ValueOrDefault!)
            : null;

        if (occurrences is not null)
        {
            ReplaceIfChanged(UpcomingEvents, occurrences);
        }

        return ContentState.FromApiResult(result);
    }

    private IReadOnlyList<MasjidEventOccurrence> GetUpcoming(List<MasjidPost> events)
    {
        var from = DateTimeOffset.Now;
        return _eventsService.GetUpcoming(events, from, from.AddDays(30));
    }

    /// <summary>
    /// Overwrites <paramref name="target"/> with <paramref name="items"/> only when the
    /// contents differ, so a cache-satisfied reload leaves the bound list (and its scroll
    /// position) untouched.
    /// </summary>
    private static void ReplaceIfChanged<T>(ObservableCollection<T> target, IReadOnlyList<T> items)
    {
        if (target.SequenceEqual(items))
        {
            return;
        }

        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    /// <summary>Selects a category strip item by its zero-based <paramref name="index"/>.</summary>
    [RelayCommand]
    private void SelectCategory(int index)
    {
        SelectedCategoryIndex = index;
    }

    partial void OnSelectedCategoryIndexChanged(int value)
    {
        // Lazily load About content when the category gains focus (API-06, API-10).
        if (SelectedCategory == HomeShellCategory.About && About.LoadCommand.CanExecute(null))
        {
            About.LoadCommand.Execute(null);
        }
    }

    /// <summary>
    /// Rebuilds the category strip and swipe pages from the active masjid's feature
    /// flags (API-06): disabled features disappear from the strip, and a removed
    /// selected category falls back to the first visible page. Only categories with
    /// an always-visible counterpart (About) can be removed without leaving the
    /// carousel empty.
    /// </summary>
    private void ApplyFeatureFlags(MasjidConfig config)
    {
        var announcementsEnabled = config.FeatureFlags?.Announcements == true;
        var eventsEnabled = config.FeatureFlags?.Events == true;

        IsAnnouncementsEnabled = announcementsEnabled;
        IsEventsEnabled = eventsEnabled;

        var visible = _allCategoryPages
            .Where(page => page.Category switch
            {
                HomeShellCategory.Announcements => announcementsEnabled,
                HomeShellCategory.Events => eventsEnabled,
                _ => true,
            })
            .ToList();

        var selectedCategory = SelectedCategory;
        var structureChanged = visible.Count != CategoryPages.Count ||
            visible.Where((page, i) => CategoryPages[i].Category != page.Category).Any();

        if (structureChanged)
        {
            CategoryPages.Clear();
            foreach (var page in visible)
            {
                CategoryPages.Add(page);
            }

            var newIndex = selectedCategory is null
                ? -1
                : visible.FindIndex(page => page.Category == selectedCategory);
            SelectedCategoryIndex = newIndex >= 0 ? newIndex : 0;

            // A same-index selection can resolve to a different category after the
            // rebuild, so re-assert the strip highlights (MVVM toolkit skips setters
            // that do not change the stored index).
            OnPropertyChanged(nameof(IsTodaySelected));
            OnPropertyChanged(nameof(IsEventsSelected));
            OnPropertyChanged(nameof(IsAboutSelected));
        }

        // The clamp above can land on About without changing the stored index.
        if (SelectedCategory == HomeShellCategory.About && About.LoadCommand.CanExecute(null))
        {
            About.LoadCommand.Execute(null);
        }
    }

    /// <summary>Navigates to the full Prayer Times page.</summary>
    [RelayCommand]
    private Task GoToPrayerTimesAsync() => _navigation.GoToAsync(Routes.PrayerTimes);

    /// <summary>Navigates to the Settings page (FR-ST-01).</summary>
    [RelayCommand]
    private Task GoToSettingsAsync() => _navigation.GoToAsync(Routes.Settings);

    /// <summary>Navigates to the remote notification inbox.</summary>
    [RelayCommand]
    private Task GoToNotificationsAsync() => _navigation.GoToAsync(Routes.Notifications);

    /// <summary>Navigates to the unified detail page for the selected post.</summary>
    [RelayCommand]
    private Task SelectPostAsync(MasjidPost post)
    {
        return _navigation.GoToAsync(
            Routes.PostDetailFromHome,
            new Dictionary<string, object> { { nameof(PostDetailViewModel.Post), post } });
    }

    /// <summary>Navigates to the event detail page for the selected occurrence.</summary>
    [RelayCommand]
    private Task SelectOccurrenceAsync(MasjidEventOccurrence occ)
    {
        return _navigation.GoToAsync(
            Routes.PostDetailFromHome,
            new Dictionary<string, object>
            {
                { nameof(PostDetailViewModel.Post), occ.Event },
                { nameof(PostDetailViewModel.Occurrence), occ },
            });
    }

    /// <summary>
    /// Opens a post targeted by a notification. The deep-link type selects the matching
    /// rendered and durable feed cache; only a cache miss calls <c>GET /posts/{id}</c>.
    /// </summary>
    public Task<bool> OpenPostAsync(PostDeepLink deepLink, CancellationToken ct = default) =>
        OpenPostAsync(deepLink, Routes.PostDetailFromHome, ct);

    /// <summary>
    /// Opens a notification post on the current navigation stack so Back returns to the inbox.
    /// </summary>
    public Task<bool> OpenPostFromCurrentStackAsync(
        PostDeepLink deepLink,
        CancellationToken ct = default) =>
        OpenPostAsync(deepLink, Routes.PostDetail, ct);

    private async Task<bool> OpenPostAsync(
        PostDeepLink deepLink,
        string detailRoute,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(deepLink);
        LogDeepLinkResolutionStarted(_logger, deepLink.MasjidId, deepLink.Type, deepLink.PostId);

        var configResult = await _context.GetConfigAsync(ct: ct);
        var config = configResult?.ValueOrDefault;
        if (config is null ||
            !string.Equals(config.Masjid?.Id, deepLink.MasjidId, StringComparison.Ordinal))
        {
            LogDeepLinkResolutionRejected(_logger, deepLink.PostId, "tenant mismatch or missing config");
            return false;
        }

        // Feature-flagged feeds must not be reachable through notifications either (API-06).
        var featureEnabled = deepLink.Type switch
        {
            PostDeepLinkType.Announcements => config.FeatureFlags?.Announcements == true,
            PostDeepLinkType.Events => config.FeatureFlags?.Events == true,
            _ => false,
        };
        if (!featureEnabled)
        {
            LogDeepLinkResolutionRejected(_logger, deepLink.PostId, "feature disabled by configuration");
            return false;
        }

        var feedUrl = deepLink.Type switch
        {
            PostDeepLinkType.Announcements => config.Endpoints?.AnnouncementsUrl,
            PostDeepLinkType.Events => config.Endpoints?.EventsUrl,
            _ => null,
        };
        if (string.IsNullOrWhiteSpace(feedUrl))
        {
            LogDeepLinkResolutionRejected(_logger, deepLink.PostId, "missing feed URL");
            return false;
        }

        var (post, occurrence) = await FindCachedPostAsync(deepLink, feedUrl, ct);
        if (!MatchesDeepLink(post, deepLink))
        {
            var result = await _postDetailsService.GetPostAsync(
                feedUrl,
                deepLink.PostId,
                ApiCachePolicy.PreferCache,
                ct);
            post = result.ValueOrDefault;
        }

        if (!MatchesDeepLink(post, deepLink))
        {
            LogDeepLinkResolutionRejected(_logger, deepLink.PostId, "post unavailable or ID mismatch");
            return false;
        }

        occurrence ??= CreateOccurrence(post);
        if (occurrence is not null)
        {
            await NavigateToOccurrenceAsync(occurrence, detailRoute);
        }
        else
        {
            await NavigateToPostAsync(post, detailRoute);
        }

        LogDeepLinkNavigationSucceeded(_logger, deepLink.PostId);
        return true;
    }

    private Task NavigateToPostAsync(MasjidPost post, string route) =>
        _navigation.GoToAsync(
            route,
            new Dictionary<string, object> { { nameof(PostDetailViewModel.Post), post } });

    private Task NavigateToOccurrenceAsync(MasjidEventOccurrence occurrence, string route) =>
        _navigation.GoToAsync(
            route,
            new Dictionary<string, object>
            {
                { nameof(PostDetailViewModel.Post), occurrence.Event },
                { nameof(PostDetailViewModel.Occurrence), occurrence },
            });

    private async Task<(MasjidPost? Post, MasjidEventOccurrence? Occurrence)> FindCachedPostAsync(
        PostDeepLink deepLink,
        string feedUrl,
        CancellationToken ct)
    {
        if (deepLink.Type == PostDeepLinkType.Announcements)
        {
            var rendered = RecentAnnouncements.FirstOrDefault(post => post.PostId == deepLink.PostId);
            if (rendered is not null)
            {
                return (rendered, null);
            }

            var cached = await _announcementsService.GetAnnouncementsAsync(
                feedUrl,
                ApiCachePolicy.CacheOnly,
                ct);
            return (cached.ValueOrDefault?.FirstOrDefault(post => post.PostId == deepLink.PostId), null);
        }

        var renderedOccurrence = UpcomingEvents.FirstOrDefault(
            item => item.Event.PostId == deepLink.PostId);
        if (renderedOccurrence is not null)
        {
            return (renderedOccurrence.Event, renderedOccurrence);
        }

        var cachedEvents = await _eventsService.GetEventsAsync(
            feedUrl,
            ApiCachePolicy.CacheOnly,
            ct);
        var cachedPost = cachedEvents.ValueOrDefault?.FirstOrDefault(
            post => post.PostId == deepLink.PostId);
        return (cachedPost, CreateOccurrence(cachedPost));
    }

    private static MasjidEventOccurrence? CreateOccurrence(MasjidPost? post)
    {
        if (post is not { IsEvent: true, EventDateTime: { } eventDateTime })
        {
            return null;
        }

        var activeAlert = !string.IsNullOrWhiteSpace(post.Alert) &&
            post.AlertEndDateTime >= DateTimeOffset.UtcNow
                ? post.Alert
                : null;
        return new MasjidEventOccurrence(post, eventDateTime, activeAlert);
    }

    private static bool MatchesDeepLink(
        [NotNullWhen(true)] MasjidPost? post,
        PostDeepLink deepLink) =>
        post is not null &&
        post.PostId == deepLink.PostId;

    private void OnNotificationInboxChanged(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(UpdateUnreadNotifications);

    private void UpdateUnreadNotifications()
    {
        var masjidId = _context.ActiveEntry?.Id;
        HasUnreadNotifications = masjidId is not null &&
            _notificationInbox.GetUnreadCount(masjidId) > 0;
    }

    // ─── Upcoming prayer event timer (FR-PT-02) ─────────────────────────────────

    /// <summary>Starts the per-second countdown ticker. Call from <c>OnAppearing</c>.</summary>
    public void StartCountdownTimer(IDispatcherTimer timer)
    {
        ArgumentNullException.ThrowIfNull(timer);
        _countdownTimer = timer;
        _countdownTimer.Interval = TimeSpan.FromSeconds(1);
        _countdownTimer.Tick += OnCountdownTick;
        _countdownTimer.Start();
    }

    /// <summary>Stops and detaches the countdown ticker. Call from <c>OnDisappearing</c>.</summary>
    public void StopCountdownTimer()
    {
        if (_countdownTimer is not null)
        {
            _countdownTimer.Stop();
            _countdownTimer.Tick -= OnCountdownTick;
            _countdownTimer = null;
        }
    }

    private void OnCountdownTick(object? sender, EventArgs e) => UpdateCountdown();

    private void UpdateCountdown()
    {
        if (TodaySchedule is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var target = PrayerCountdownSelector.NextEvent(_countdownSchedules, now);
        UpcomingPrayerEventText = target is null
            ? string.Empty
            : PrayerEventCountdownFormatter.Format(target);
    }

    private void RebuildHomePrayerRows(DailyPrayerSchedule schedule)
    {
        var use24HourTime = _preferences.GetValue(TimeFormatKey, false);
        HomePrayerRows =
        [
            HomePrayerTimeRow.Create("Fajr", schedule.FajrAthan, schedule.FajrIqama, use24HourTime),
            HomePrayerTimeRow.Create("Sunrise", schedule.Sunrise, null, use24HourTime),
            HomePrayerTimeRow.Create("Dhuhr", schedule.DhuhrAthan, schedule.DhuhrIqama, use24HourTime),
            HomePrayerTimeRow.Create("Asr", schedule.AsrAthan, schedule.AsrIqama, use24HourTime),
            HomePrayerTimeRow.Create("Maghrib", schedule.MaghribAthan, schedule.MaghribIqama, use24HourTime),
            HomePrayerTimeRow.Create("Isha", schedule.IshaAthan, schedule.IshaIqama, use24HourTime),
        ];
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Home data load failed; showing available cached content.")]
    private static partial void LogHomeLoadFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Prayer times or their dependent background updates failed.")]
    private static partial void LogPrayerLoadFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Prayer widget or reminder updates failed after prayer times loaded.")]
    private static partial void LogPrayerDependentUpdatesFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Announcements load failed; preserving the existing feed.")]
    private static partial void LogAnnouncementsLoadFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Events load failed; preserving the existing feed.")]
    private static partial void LogEventsLoadFailed(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, Message = "Resolving post deep link. MasjidId={MasjidId}, Type={Type}, PostId={PostId}")]
    private static partial void LogDeepLinkResolutionStarted(ILogger logger, string masjidId, PostDeepLinkType type, int postId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Post deep link resolution rejected. PostId={PostId}, Reason={Reason}")]
    private static partial void LogDeepLinkResolutionRejected(ILogger logger, int postId, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Post deep link navigation succeeded. PostId={PostId}")]
    private static partial void LogDeepLinkNavigationSucceeded(ILogger logger, int postId);
}

/// <summary>Compact iqama-first prayer time shown in the Home prayer card.</summary>
public sealed record HomePrayerTimeRow(
    string Name,
    string PrimaryTimeText,
    string SecondaryTimeText)
{
    public static HomePrayerTimeRow Create(
        string name,
        TimeOnly athan,
        TimeOnly? iqama,
        bool use24HourTime) =>
        new(
            name,
            PrayerTimeFormatter.Format(iqama ?? athan, use24HourTime, includeMeridiem: false),
            iqama.HasValue
                ? PrayerTimeFormatter.Format(athan, use24HourTime, includeMeridiem: false)
                : string.Empty);
}
