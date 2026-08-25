# Masjid App Design

This document describes the implementation as it exists on August 3, 2026. The product requirements remain authoritative in [REQUIREMENTS.md](REQUIREMENTS.md); this document records the architecture, feature behavior, and known gaps in the current codebase.

## Product shape

Masjid App is a multi-tenant .NET MAUI application for iOS, Android, and Mac Catalyst. One masjid is active at a time. Its configuration supplies identity, branding, endpoint URLs, contact details, donation URL, social links, and feature flags. The mobile app combines prayer times, announcements, events, reminders, Qibla, donations, settings, and native home-screen widgets.

The solution has three main parts:

- [`MasjidApp.Core`](../src/MasjidApp.Core/) contains platform-neutral models, API and cache services, prayer calculations, RRULE expansion, sanitization, observability contracts, and widget selection logic.
- [`MasjidApp`](../src/MasjidApp/) is the MAUI application head. It owns XAML views, ViewModels, dependency injection, navigation, device APIs, notifications, and platform integrations.
- [`MasjidApp.Core.Tests`](../tests/MasjidApp.Core.Tests/) tests core behavior and the mock API contracts. The lightweight WordPress implementation is in [`backend/wp-content/plugins/masjid-api`](../backend/wp-content/plugins/masjid-api/).

Dependencies are registered in [`MauiProgram.cs`](../src/MasjidApp/MauiProgram.cs). Views use MVVM with CommunityToolkit.Mvvm, constructor-injected services, and thin code-behind.

## Startup, tenant, and navigation

[`App.xaml.cs`](../src/MasjidApp/App.xaml.cs) chooses the initial root from persisted masjid state. A first run without an active masjid opens the non-dismissable setup flow. Setup and later masjid changes use the same URL-based configuration workflow through [`AddMasjidViewModel`](../src/MasjidApp/ViewModels/AddMasjidViewModel.cs). When the submitted website returns HTTP 200 but its configuration endpoint does not, first-run setup identifies the unsupported site, scrolls the explanation into view, and links to the MasjidFeed information page.

[`IMasjidRegistry`](../src/MasjidApp.Core/Services/IMasjidRegistry.cs) persists known masjids, while [`IMasjidContext`](../src/MasjidApp.Core/Services/IMasjidContext.cs) exposes the active entry and configuration. Active-masjid changes cause dependent screens, navigation visibility, reminders, and widget data to refresh.

[`AppShell.xaml`](../src/MasjidApp/AppShell.xaml) provides the primary tabs for Home, Donate, Prayer, and Qibla. Donate is hidden when the active configuration disables donations or has no valid donation URL. Tapping Home's prayer card selects the existing Prayer tab rather than pushing a duplicate page onto the Home stack. Settings and detail pages are pushed routes registered in [`AppShell.xaml.cs`](../src/MasjidApp/AppShell.xaml.cs).

On Android, the custom plain-white splash theme inherits MAUI's application theme after launch. Theme-qualified status/navigation bar and Shell host colors keep the camera-cutout and gesture insets aligned with light/dark app surfaces, while [`ThemeService`](../src/MasjidApp/Services/ThemeService.cs) updates system-bar icon appearance when the theme changes without recreating the activity.

Home is an aggregate screen implemented by [`HomeShellPage.xaml`](../src/MasjidApp/Views/HomeShellPage.xaml) and [`HomeShellViewModel`](../src/MasjidApp/ViewModels/HomeShellViewModel.cs):

- **Today** shows a prayer summary card and recent announcements. The white card pairs six icon-labeled, abbreviated, iqama-first prayer columns with a primary-color status banner naming whichever athan or iqama occurs next. The status is minute-level rather than a live seconds countdown, and compact prayer times omit AM/PM in 12-hour mode.
- **Events** shows upcoming expanded event occurrences.
- **About** shows the active masjid's contact and social information.

Full prayer times are also available as a primary tab. Announcement and event details are pushed from the home feed.

The Today and Events tabs each wrap their list in a `RefreshView` so pull-to-refresh reaches the vertically scrolling list; a `RefreshView` placed around the swipe carousel instead binds its refresh control to the horizontal carousel scroller and never receives the gesture. `RefreshView.IsRefreshing` binds to a dedicated user-refresh property rather than the general busy indicator, preventing programmatic loading state from re-entering and cancelling the refresh command. Feed updates follow [`FeedRefreshPolicy`](../src/MasjidApp.Core/Http/FeedRefreshPolicy.cs): a load that returns no value leaves the rendered items in place instead of blanking them. Prayer/widget/reminder maintenance continues after the cached prayer card is populated, so notification scheduling cannot hold Home in its loading state. The active window cancels Home data commands when it stops, waits for active SQLite operations, and closes the pooled cache connection under iOS background execution time so no database lock survives suspension. `Window.Resumed` waits for that close before forwarding to the visible Home page because MAUI resume does not raise `Page.Appearing`; this reload lazily reopens the cache and recalculates prayer "today" in the masjid timezone after a suspended day change.

Home's root `Grid` opts out of the bottom safe area (`SafeAreaEdges="Container,Container,Container,None"`) so its lists run edge to edge and scroll underneath the floating tab bar, matching the iOS convention already exhibited by Prayer Times and Settings. The opt-out belongs on the layout, not the page: `ContentPage` already defaults to `SafeAreaEdges.None`, whereas `Layout` defaults to `SafeAreaEdges.Container`, and on iOS a layout that obeys the safe area shrinks its own frame - so the root `Grid` stopped at the tab bar and hard-clipped the last item regardless of anything set on the page. Prayer Times and Settings avoid this implicitly because their root is a `ScrollView`, which applies the safe area as a content inset instead of shrinking its frame. Each tab supplies its own 96&nbsp;px bottom padding (a transparent `CollectionView.Footer` for Today and Events, `Padding` on the About stack) so the final item can still be scrolled clear of the tab bar. Top, left, and right stay on `Container` so the branding header clears the status bar and notch.

## Configuration and resilient data access

[`MasjidConfig`](../src/MasjidApp.Core/Models/MasjidConfig.cs) is the per-masjid contract. Endpoint resolution supports absolute URLs and URLs relative to the configuration origin. Remote JSON uses source-generated `System.Text.Json` metadata and nullable fields so unknown or partial data can degrade safely.

All JSON feature services use [`IMasjidApiClient`](../src/MasjidApp.Core/Http/IMasjidApiClient.cs). Its implementation provides:

- A platform- and release-specific `User-Agent` in `MasjidApp-{platform}/{version}` format on JSON, CSV, and push-registration requests.
- HTTPS validation and request timeouts.
- Polly retry with exponential backoff for transient failures.
- Conditional requests using ETag and Last-Modified metadata.
- SQLite persistence through [`SqliteCacheStore`](../src/MasjidApp.Core/Caching/SqliteCacheStore.cs).
- Stale-while-revalidate startup: any valid cached response is returned immediately, while one conditional refresh per endpoint runs in the background for the app session.
- Blocking network access only when no usable cache exists or the caller explicitly requests `ApiCachePolicy.ForceRefresh` (pull-to-refresh).
- [`ApiResult<T>`](../src/MasjidApp.Core/Http/ApiResult.cs) states for fresh, fresh cached, not-modified, stale cached, and failed results.

SQLite is the single durable response cache because it stores payloads and HTTP metadata consistently across larger documents and process restarts. Cache reads, writes, and lifecycle closure are serialized so the app can drain active work and close SQLite before iOS suspension, then reopen it lazily without losing persisted data. User preferences remain in MAUI Preferences; they are not used as a second content cache.

Feature ViewModels translate API results into [`ContentState`](../src/MasjidApp.Core/Observability/ContentState.cs), including last-updated and stale state. Cached configuration, SalahAPI, announcements, events, and other JSON content therefore render before network access on startup. The only cold-start exception is an endpoint with no valid cached response, which must be fetched before that feature can render. Explicit pull-to-refresh waits for the server and updates the current view.

## Prayer times

The prayer pipeline is orchestrated by [`PrayerTimesProvider`](../src/MasjidApp.Core/PrayerTimes/PrayerTimesProvider.cs):

1. [`SalahApiService`](../src/MasjidApp.Core/SalahApi/SalahApiService.cs) obtains the active masjid's SalahAPI document through the resilient JSON client.
2. [`PrayerTimeCalculator`](../src/MasjidApp.Core/PrayerTimes/PrayerTimeCalculator.cs) uses the SalahAPI calculation library and the document's IANA timezone, coordinates, method, Asr method, and high-latitude adjustment.
3. Iqama rules and Jumuah sessions are applied. Ramadan overrides take precedence over daylight-saving overrides, which take precedence over base rules.
4. [`CsvPrayerTimesService`](../src/MasjidApp.Core/PrayerTimes/CsvPrayerTimesService.cs) supplies configured daily rows from [`SqliteCsvPrayerTimesStore`](../src/MasjidApp.Core/PrayerTimes/SqliteCsvPrayerTimesStore.cs) and refreshes that store in the background of the same call. Overrides are always read from SQLite, never straight from the network. [`CsvPrayerTimesParser`](../src/MasjidApp.Core/PrayerTimes/CsvPrayerTimesParser.cs) accepts header-based and positional formats and skips malformed rows.
5. [`PrayerTimesMerger`](../src/MasjidApp.Core/PrayerTimes/PrayerTimesMerger.cs) lets CSV athan values override calculated values. CSV iqama values win when present; otherwise calculated iqama remains.

The resulting [`DailyPrayerSchedule`](../src/MasjidApp.Core/PrayerTimes/DailyPrayerSchedule.cs) carries the masjid timezone, six daily times, iqama values, and Jumuah sessions. "Today" is derived from that timezone rather than the device calendar date. [`PrayerCountdownSelector`](../src/MasjidApp.Core/PrayerTimes/PrayerCountdownSelector.cs) selects the next athan-or-iqama transition and the countdown-card sky period. The app sky period matches widget behavior: Sunrise and Maghrib use ±15-minute windows, with Fajr, Dhuhr, Asr, and Isha between them.

[`PrayerTimesPage.xaml`](../src/MasjidApp/Views/PrayerTimesPage.xaml) presents today's schedule, countdown, Jumuah sessions, and a multi-day schedule. The Jumuah card sits directly beneath today's times and lists every session in `jumuahRules` with its name, time, and location; it renders on every day of the week and is hidden only when the masjid publishes no sessions. Home uses the same provider and countdown logic. Time formatting follows the persisted 12/24-hour preference. Returning to the Prayer tab rechecks cache-backed data without showing the refresh indicator or rebuilding unchanged rows; the first load and explicit pull-to-refresh still show progress.

The SalahAPI document is available offline from SQLite and calculated schedules can be regenerated from it.

CSV overrides are persisted in the same `masjidapp_cache.db` file under a dedicated `csv_prayer_times` table shaped like the feed itself: one row per day holding the athan and iqama value for each prayer. A singleton `csv_prayer_times_feed` row records the feed URL, its `ETag`, the last check time, and the last successful download time.

[`CsvPrayerTimesService`](../src/MasjidApp.Core/PrayerTimes/CsvPrayerTimesService.cs) applies this refresh policy:

- A network call is attempted only when the stored rows cover fewer than 30 future days, when the feed has not been checked for 24 hours, or when the caller passes `ApiCachePolicy.ForceRefresh` (pull-to-refresh). Normal loads return SQLite rows before that request completes; forced refresh waits for it. A coverage shortfall always wins over the daily throttle.
- Refresh is a single conditional `GET` carrying `If-None-Match`, requesting `fromDate` today through `toDate` six months out. `304 Not Modified` only advances the check timestamp.
- The day table is replaced inside one transaction, and only after a `200` response parsed into at least one row. A failed, empty, or unparseable response leaves the previous schedule intact and is metered as a degraded `csv_fetch` or `csv_parse` operation. Only cancellation requested by the caller is rethrown.
- The stored feed URL is compared against the active masjid's `csvUrl` on every read. A mismatch resets the table and validator before anything is served, so one masjid never renders another's schedule (`API-06`).

When a CSV feed is configured but the stored rows cannot cover the window on screen, [`PrayerTimesProvider`](../src/MasjidApp.Core/PrayerTimes/PrayerTimesProvider.cs) returns the merged schedule as `ApiResult.Stale`. The calculated fallback still renders, but `ContentState` reports it as offline or stale instead of passing calculated estimates off as the masjid's published schedule.

## Events and announcements

[`EventsService`](../src/MasjidApp.Core/Events/EventsService.cs) fetches cached events and exposes upcoming, category, and search operations. Event definitions may include an `alert` and `alertEndDateTime`; when both are present and the query evaluation time has not passed the inclusive end instant, the service carries the active alert onto every concrete occurrence for consistent list and detail rendering. Missing or expired alert windows are hidden. [`RRuleExpander`](../src/MasjidApp.Core/Events/RRuleExpander.cs) uses iCal.Net to expand bounded daily, weekly, and monthly recurrence rules, including COUNT and UNTIL. Invalid rules return no occurrences rather than crashing.

[`HomeShellPage.xaml`](../src/MasjidApp/Views/HomeShellPage.xaml) and [`PostDetailPage.xaml`](../src/MasjidApp/Views/PostDetailPage.xaml) display active event alerts in an accessible, high-contrast update panel. The unified detail page exposes the original post permalink through a safe system-browser action and the native platform share sheet, and conditionally renders event date, recurrence, location, alert, and calendar controls when `IsEvent` is true. Actions are hidden when a post has no permalink. Event navigation supplies a concrete [`MasjidEventOccurrence`](../src/MasjidApp.Core/Events/MasjidEventOccurrence.cs), so the calendar editor uses the selected expanded occurrence rather than only the series start. Users can configure an alert in that editor; the app does not currently expose a separate event-reminder action (`FR-EV-05`). The model has no duration, so calendar drafts use a one-hour duration.

[`AnnouncementsService`](../src/MasjidApp.Core/Announcements/AnnouncementsService.cs) fetches cached announcements. Event and announcement HTML passes through the strict allow-list in [`HtmlSanitizer`](../src/MasjidApp.Core/Utils/HtmlSanitizer.cs) before rendering. Scripts, event-handler attributes, CSS, and non-HTTP(S) links are removed. Safe HTTP(S) links open in the system browser and encoded Unicode entities remain displayable.

Both services normalize every fetched item before returning it: relative image paths are resolved against the masjid base URL, and titles are converted to plain text so WordPress-encoded punctuation (for example `&#8211;`) renders as real characters instead of raw entities. Event alerts are also converted to sanitized plain text before display.

Events, announcements, and `GET /posts/{id}` deserialize into one concrete [`MasjidPost`](../src/MasjidApp.Core/Models/MasjidPost.cs) transport model, including the persisted `postId` used by push deep links and nullable event-only fields. The event service normalizes feed items to `IsEvent = true` for compatibility with older payloads that omitted the discriminator. Recurrence expansion remains a separate domain concern represented by [`MasjidEventOccurrence`](../src/MasjidApp.Core/Events/MasjidEventOccurrence.cs). [`PostDeepLink`](../src/MasjidApp.Core/Push/PostDeepLink.cs) accepts only the versioned `masjidapp://open/v1/masjids/{masjidId}/{events|announcements}/{postId}` contract. Links for another tenant are ignored rather than switching the active masjid. [`HomeShellViewModel`](../src/MasjidApp/ViewModels/HomeShellViewModel.cs) uses the URI type only to select the matching rendered collection and durable feed cache; a miss fetches `GET /posts/{id}` through the resilient client. The returned post's `IsEvent` value controls event-only detail UI, because an announcement notification may target a post that also has event metadata. Every valid result opens the unified post detail page. Deleted, malformed, tenant-mismatched, or ID-mismatched posts remain on the current screen without crashing.

## Community features

- [`AboutViewModel`](../src/MasjidApp/ViewModels/AboutViewModel.cs) powers the Home/About contact, map, website, and social actions. It resolves the active masjid's configured address through [`IMapLocationResolver`](../src/MasjidApp/Services/IMapLocationResolver.cs) and clears the previous coordinates before every tenant reload.
- [`LocationMapPreview`](../src/MasjidApp/Controls/LocationMapPreview.xaml.cs) renders a read-only native MapKit preview on iOS/Mac Catalyst and Google Maps preview on configured Android builds. It centers one masjid pin, leaves gestures to the parent `ScrollView`, and delegates taps to the existing full maps-app action. Failed geocoding or a missing Android SDK key shows the branded address fallback instead of a blank map.
- [`ContactPage.xaml`](../src/MasjidApp/Views/ContactPage.xaml) provides a separate contact route with call, email, map, and browser actions.
- [`QiblaCalculations`](../src/MasjidApp.Core/Utils/QiblaCalculations.cs) computes the great-circle bearing to the Kaaba and normalized dial/needle rotations. [`QiblaViewModel`](../src/MasjidApp/ViewModels/QiblaViewModel.cs) combines location permission and magnetometer updates, and degrades to a status message when unavailable.
- [`DonationsPage`](../src/MasjidApp/Views/DonationsPage.xaml) renders the configured HTTPS donation page in an embedded WebView and applies the app's effective light/dark scheme to the loaded document. Same-origin HTTPS navigation remains inline, while other HTTPS origins open in the system-preferred browser and insecure navigation is blocked. The app never inspects or handles payment credentials.

## Notifications

Local prayer reminders are abstracted by [`IPrayerReminderScheduler`](../src/MasjidApp.Core/Notifications/IPrayerReminderScheduler.cs) and implemented with Plugin.LocalNotification in [`PrayerReminderScheduler`](../src/MasjidApp/Notifications/PrayerReminderScheduler.cs). [`PrayerReminderSettingsService`](../src/MasjidApp/Services/PrayerReminderSettingsService.cs) persists the global iqama toggle and lead time, cancels stale pending notifications, and schedules all available iqamas across a rolling six-day window using stable IDs and each schedule's masjid timezone. The 60 reserved athan/iqama slots remain below iOS's practical pending-notification cap. Permission denial and unexpected platform scheduling failures are non-fatal; Settings surfaces them as non-blocking states and unexpected failures are logged.

Event detail uses a separate local reminder path for the selected occurrence. The Settings screen exposes a global iqama-reminder toggle and lead-time picker. Changes are applied immediately, while the Home shell refills the rolling reminder window whenever prayer schedules load or refresh. Per-prayer controls and sound selection remain future `FR-PT-10` work.

Reminder text is composed by [`PrayerReminderMessage`](../src/MasjidApp.Core/Notifications/PrayerReminderMessage.cs) from localized templates that the app head reads out of `AppResources.resx`, keeping the wording localizable (`TC-05`) while the composition rules stay platform-neutral and unit-tested. Every reminder shares the title "Get ready for salah!" and carries a body naming the prayer, the event, the lead time, and the prayer time, such as "Fajr iqama in 10 minutes (5:30 AM)". A zero lead time drops to "Fajr iqama at 5:30 AM", lead times are pluralized and rendered in hours past sixty minutes, and the embedded time follows the user's 12/24-hour preference (`FR-ST-03`). Prayer names are not yet localized, matching the rest of the prayer UI.

Home-shell refreshes are idempotent. [`PrayerReminderFingerprint`](../src/MasjidApp.Core/Notifications/PrayerReminderFingerprint.cs) hashes the enabled flag, lead time, active masjid identifier, window start date, 12/24-hour preference, and every scheduled iqama time plus its timezone. `PrayerReminderSettingsService` persists that fingerprint through `IUserPreferences`, and `SyncAsync` returns `Unchanged` without cancelling or re-posting notifications when it matches. Including the window start date forces exactly one refill per day rollover, including the iqama times forces a reschedule when CSV overrides or Ramadan rules move a time, and including the clock preference refreshes the text baked into pending notifications. Explicit settings changes through `UpdateAsync` clear the fingerprint first so they always reschedule, and disabling reminders or being denied permission also invalidates it. On Android, `RECEIVE_BOOT_COMPLETED` is declared so Plugin.LocalNotification's boot receiver can restore pending alarms after a reboot.

Remote push uses Firebase Cloud Messaging on iOS and Android through [`PushNotificationCoordinator`](../src/MasjidApp/Push/PushNotificationCoordinator.cs). Settings exposes an independent Announcements switch with the description "Notify me of important updates." Enabling it requests the shared OS notification permission and registers the FCM token; disabling it sends `DELETE /push/registrations` without disabling local prayer reminders. Registration sends a Firebase App Check token, uses the API-provided per-tenant Firebase options, and stores the FCM token plus registration state in platform secure storage. Debug builds use Firebase's debug App Check provider; Release uses App Attest on iOS and Play Integrity on Android.

Tenant switches first unregister from the old tenant while its Firebase project is still active, delete the old default Firebase app, initialize the new tenant's project, and register its token. Token refresh posts both the new and previous token so the server can complete rotation. If old-tenant unregistration fails, the saved state is retained and new registration is deferred rather than silently leaving two active tenant subscriptions. iOS uses one notification-center delegate that dispatches local reminders to Plugin.LocalNotification and remote taps to Firebase, including taps that cold-start the app before Firebase finishes initializing. Custom URI activation routes through the same deep-link router, and notification deduplication is persisted only after navigation succeeds. iOS uses App Group-only entitlements for ad-hoc simulator builds, development APS entitlements for Debug device builds, and production APS plus App Attest entitlements for Release; Android registers the Firebase receivers and a community-updates notification channel.

Notification receive and tap callbacks write a tenant-isolated inbox through [`NotificationInboxService`](../src/MasjidApp/Notifications/NotificationInboxService.cs), deduplicated by `notificationId` and capped at the latest 100 entries per masjid. Home imports OS-delivered pushes on load/resume so its bell can show a red unread indicator before the inbox is opened. [`NotificationsPage`](../src/MasjidApp/Views/NotificationsPage.xaml) imports and clears delivered OS banners without changing inbox read state. Flat rows use primary-color event/announcement icon tiles and inset dividers. Selecting a row marks it read and pushes post detail on the current stack, so Back returns to Notifications; its swipe action deletes it. History remains until individually deleted or removed through the gray-outlined Clear all action.

Notification taps consume the documented string-valued `data.deepLink`; `notificationId` suppresses duplicate foreground presentation and in-app tap processing on a best-effort basis. After an OS notification tap is accepted for navigation, its tenant inbox entry is marked read, which updates the Home bell indicator through the inbox change event. Android additionally forwards cold-start and warm launch-intent data from `MainActivity` directly to the coordinator so Firebase tap events cannot be lost before event subscription. When Firebase strips display title/body from that launch intent, the coordinator hydrates the inbox entry from cached `GET /posts/{id}` content after navigation. Custom URI activation, OS notification taps, and inbox item taps share [`DeepLinkRouter`](../src/MasjidApp/Push/DeepLinkRouter.cs), which queues cold-start navigation until Shell is available. Post links fetch the target directly through [`PostDetailsService`](../src/MasjidApp.Core/Posts/PostDetailsService.cs) and `GET /posts/{id}` rather than depending on the item still appearing in the bounded announcements/events feed. Rich iOS pushes embed a native Swift Notification Service Extension that accepts HTTPS image URLs from FCM's `fcm_options.image`, downloads them with bounded timeouts, and falls back to the unmodified notification on failure. OS-level duplicate display while the app is terminated still requires collapse semantics in the server's APNs/FCM send request.

## Native widgets

Widgets never call the network. [`WidgetRefreshService`](../src/MasjidApp/Widgets/WidgetRefreshService.cs) builds a seven-day [`WidgetData`](../src/MasjidApp.Core/Widgets/WidgetData.cs) snapshot starting from the masjid's current local date. [`WidgetDataWriter`](../src/MasjidApp/Widgets/WidgetDataWriter.cs) writes it to Android SharedPreferences or the iOS App Group container and requests a widget refresh.

[`WidgetDaySelector`](../src/MasjidApp.Core/Widgets/WidgetDaySelector.cs) is the shared reference for selecting the applicable day, current prayer, next prayer, and stale state. Native implementations mirror that behavior:

- iOS WidgetKit code in [`MasjidWidget.swift`](../src/MasjidApp/Platforms/iOS/MasjidWidget/MasjidWidget.swift) provides small daily-iqama and next-prayer-countdown widgets, medium compact and large full-day widgets, and an iOS 16+ Lock Screen circular gauge. The small iOS 16+ countdown uses timer-interval progress rendered as a circular ring, so the ring and its single countdown label advance without timeline delivery. A shared buffered timeline supplies minute entries for the iOS 15 fallback and Lock Screen gauge, plus exact and one-second post-transition entries so completed targets hand off without counting upward.
- Android `RemoteViews` providers in [`Platforms/Android/Widget`](../src/MasjidApp/Platforms/Android/Widget/) provide matching 2x2 daily-iqama and next-prayer-countdown widgets, plus the existing compact and full-day widgets with `JobScheduler` refresh.

Both platforms show cached content and a stale state when the snapshot no longer covers the current time. Background refresh timing remains subject to OS scheduling. Prayer lists highlight the current athan-to-athan prayer period. Countdown widgets target athan before each prayer, switch to that prayer's iqama between athan and iqama, and show progress as the remaining fraction so it shrinks toward the selected transition. Widget backgrounds independently use tested Sunrise and Maghrib ±15-minute windows with Fajr, Dhuhr, Asr, and Isha periods between them. Full-day widgets include the design-team prayer, athan, and iqama icons. Upcoming events and notifications are not included in the widget schema.

## Settings and cross-cutting behavior

[`SettingsPage.xaml`](../src/MasjidApp/Views/SettingsPage.xaml) uses grouped native-style rows and exposes active-masjid change, Appearance and iqama reminder lead-time drill-downs, 12/24-hour time, global iqama reminders with a persisted lead time, telemetry consent, app version, and privacy policy. The lead-time row shows the current selection and opens a checkmark selection list matching the Appearance interaction.

Localization infrastructure consists of [`AppResources.resx`](../src/MasjidApp/Resources/Strings/AppResources.resx), [`LocalizationManager`](../src/MasjidApp/Localization/LocalizationManager.cs), and the [`LocalizeExtension`](../src/MasjidApp/Localization/LocalizeExtension.cs). The shipping app is English-only. Many current XAML strings remain literals, no additional locale resource exists, and there is no language selector, so runtime localization is not complete.

Views use semantic descriptions, heading levels, dynamic text sizing, and theme bindings where implemented. Accessibility and visual behavior still require device-level verification because the automated suite does not exercise the MAUI visual tree.

[`MasjidTelemetry`](../src/MasjidApp.Core/Observability/MasjidTelemetry.cs) defines activities, metrics, and degraded-operation counters without depending on the OpenTelemetry SDK. The app head configures OpenTelemetry export only after explicit user consent. No secrets or personal data belong in telemetry.

## Backend

[`masjid-api.php`](../backend/wp-content/plugins/masjid-api/masjid-api.php) registers the five public read-only v1 WordPress REST routes documented by the production plugin: configuration, SalahAPI, events, announcements, and post descriptions. Its list handlers return seed arrays that mirror the mock contracts; post descriptions use matching seed content and fall back to published WordPress posts.

The SalahAPI response advertises `/wp-json/masjid/v1/prayer-times-csv`, but that route is not registered. The app therefore falls back to calculated values against the included backend.

## Security and failure behavior

- Remote event and announcement content is sanitized before display.
- Donations require HTTPS and leave payment handling to the browser.
- API parsing ignores unknown fields and converts transport failures into typed result states.
- Location and notification permissions degrade without terminating the app.
- Sensitive values must use platform secure storage; none are embedded in source.
- Widgets consume only app-written shared data and do not have network credentials.

## Verification and current gaps

The CI workflow in [`.github/workflows/ci.yml`](../.github/workflows/ci.yml) restores, formats, builds, and tests the Core and Core.Tests projects on Linux and validates mock JSON. Tests cover API resilience, SQLite persistence, contracts, prayer calculations and overrides, CSV parsing, caching, and merging, RRULE expansion, HTML sanitization, Qibla math, and widget selection.

The following are verified limitations rather than stale checklist state:

- The WordPress CSV route is absent, so the bundled backend always falls back to calculated values.
- QR camera scanning uses ZXing.Net.MAUI from both first-run setup and Add Masjid, with manual URL entry retained as fallback.
- Prayer reminder sound and per-prayer athan/iqama choices are not exposed. Remote announcement push has one global opt-in; finer category-level push controls are not exposed.
- Language selection and full resource-backed localization are not implemented.
- UI automation, device notification tests, and widget screenshot tests are absent; CI does not build the MAUI platform heads.
- The app uses Outfit globally, Gilda Display for event/announcement headings, final design-team navigation and prayer icons, and `branding.primaryColor` for active Shell tab icons/titles. Shell's top navigation bar stays white in light mode and off-black in dark mode so tenant accents do not create a mismatched page header. Standard text buttons use a global pill-shaped style.
- All Label font sizes and weights come from a named type scale in `Resources/Styles/Styles.xaml` (`TextMicro`, `TextCaption`, `TextBody`, `TextSubhead`, `TextCardTitle`, `TextTitle`, `TextHeadline`, `TextDisplay`, each with a `…Strong` bold variant where used, plus `GlyphInline`/`GlyphSelected`/`GlyphAccessory`/`GlyphLarge` for text glyphs used as icons and `ButtonCompact`/`ButtonProminent` for buttons). Views set `Style` and never `FontSize`/`FontAttributes` inline, so text sizing can be adjusted app-wide in one place. Each scale step derives from a keyed `BaseLabel`/`BaseButton` style, because an explicit `Style` on a control suppresses the implicit one and would otherwise drop the shared font family, colors, and disabled visual state.
- `branding.primaryColor` (light mode) and `branding.primaryColorDark` (dark mode, falling back to `primaryColor`) are published at runtime as the single `ActivePrimaryColor` application resource, re-applied on every masjid switch and theme change. Brand-accented UI must bind it with `DynamicResource`; `StaticResource Primary` resolves once at parse time and would ignore the tenant color. The bottom TabBar is the one exception: `AppShell` sets `Shell.TabBarForegroundColor`/`TabBarTitleColor` directly in code (`ApplyBrandColor`) rather than via a `Styles.xaml` `DynamicResource` setter, and seeds them from a per-masjid cached color (`Preferences`) before `InitializeComponent` runs, so the native tab bar (iOS in particular) never paints the hard-coded purple default on launch or needs a navigation to pick up the real tenant color.
- Rich-content link safety, Unicode decoding, calendar draft values, cache policy, tenant matching, and branding color parsing are unit-tested; native calendar editor behavior still requires manual UX validation.
- Contact and Home/About share hardened contact actions. Home/About reuses its resolved preview coordinates when opening directions; Contact resolves on demand. Native maps select Apple Maps on iOS and an installed maps handler on Android, with an HTTPS fallback.
- Embedded maps use `Microsoft.Maui.Controls.Maps`: Apple map display needs no key, while Android receives a package/signing-restricted Google Maps SDK key through the `GoogleMapsApiKey` MSBuild property. Keyless Android builds remain valid and display the branded address fallback (`NFR-SC-01`, `NFR-RL-01`).
- App Store and Play Store publication remain operational release work.

ICOB branding bundles alternate launcher icons. Every active tenant enters an in-app splash that resolves `branding.splashLogoUrl` from cached configuration and displays a tenant-scoped, SQLite-cached image when available. It remains white while a configured image loads or fails; the bundled white app icon appears only when the configuration does not specify a splash image. The native launch screen is plain white (iOS `LaunchScreen.storyboard`; Android `maui_splash_screen.xml` plus the Android 12+ splash attributes in `values-v31/styles.xml`) so it flows into the branded splash without a colored flash after tenant resolution. The older contact checklist is superseded by the implemented Contact and Home/About surfaces.