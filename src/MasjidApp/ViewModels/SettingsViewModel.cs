using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasjidApp.Core.Services;
using MasjidApp.Core.Theming;
using MasjidApp.Localization;
using MasjidApp.Observability;
using MasjidApp.Push;
using MasjidApp.Services;
using Microsoft.Maui.ApplicationModel;

namespace MasjidApp.ViewModels;

// FR-ST-01, FR-ST-02, FR-ST-03, FR-ST-05, NFR-PR-02: settings screen aggregating
// user personalization entry points.

/// <summary>
/// ViewModel for the Settings screen. Persists theme, time format, and notification
/// preferences across launches; exposes commands to change masjid and view the privacy policy.
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private const string ThemeKey = "settings_theme";
    private const string TimeFormatKey = "settings_24h";
    private static readonly int[] PrayerReminderLeadTimes = [0, 5, 10, 15, 20, 30, 45, 60];

    private readonly IUserPreferences _preferences;
    private readonly IThemeService _themeService;
    private readonly INavigationService _navigation;
    private readonly IMasjidContext _masjidContext;
    private readonly TelemetryService _telemetry;
    private readonly IPrayerReminderSettingsService _prayerReminderSettings;
    private readonly IPushNotificationSettingsService _pushNotificationSettings;
    private readonly IAlertService _alerts;
    private readonly IDeviceNotificationSettings _deviceNotificationSettings;

    /// <summary>The currently selected app theme (FR-ST-03).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedThemeDisplay))]
    private ThemePreference _selectedTheme;

    public string SelectedThemeDisplay => SelectedTheme == ThemePreference.System
        ? "Automatic"
        : SelectedTheme.ToString();

    /// <summary>Whether the 24-hour time format is active (FR-ST-03).</summary>
    [ObservableProperty]
    private bool _is24HourTime;

    /// <summary>Whether local prayer-time reminders are enabled (FR-ST-02).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChangePrayerReminderLeadTime))]
    private bool _prayerRemindersEnabled;

    /// <summary>Localized options for how long before each iqama a reminder should fire.</summary>
    public IReadOnlyList<string> PrayerReminderLeadTimeOptions { get; } =
    [
        Localize("Settings_PrayerReminder_LeadTime_AtIqama"),
        Localize("Settings_PrayerReminder_LeadTime_5"),
        Localize("Settings_PrayerReminder_LeadTime_10"),
        Localize("Settings_PrayerReminder_LeadTime_15"),
        Localize("Settings_PrayerReminder_LeadTime_20"),
        Localize("Settings_PrayerReminder_LeadTime_30"),
        Localize("Settings_PrayerReminder_LeadTime_45"),
        Localize("Settings_PrayerReminder_LeadTime_60"),
    ];

    /// <summary>The selected index in <see cref="PrayerReminderLeadTimeOptions"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedPrayerReminderLeadTimeDisplay))]
    private int _selectedPrayerReminderLeadTimeIndex;

    /// <summary>The selected iqama reminder lead time shown in the Settings disclosure row.</summary>
    public string SelectedPrayerReminderLeadTimeDisplay =>
        PrayerReminderLeadTimeOptions[SelectedPrayerReminderLeadTimeIndex];

    /// <summary>Whether prayer reminders are currently being rescheduled.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChangePrayerReminderSettings))]
    [NotifyPropertyChangedFor(nameof(CanChangePrayerReminderLeadTime))]
    private bool _isUpdatingPrayerReminders;

    /// <summary>Whether the prayer-reminder feature is enabled for the active masjid (API-06).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChangePrayerReminderSettings))]
    [NotifyPropertyChangedFor(nameof(CanChangePrayerReminderLeadTime))]
    [NotifyPropertyChangedFor(nameof(IsPrayerRemindersUiVisible))]
    private bool _prayerRemindersFeatureEnabled = true;

    /// <summary>Whether the prayer-reminder rows should be shown in Settings.</summary>
    public bool IsPrayerRemindersUiVisible => PrayerRemindersFeatureEnabled;

    /// <summary>Whether the prayer-reminder toggle can currently be changed.</summary>
    public bool CanChangePrayerReminderSettings =>
        PrayerRemindersFeatureEnabled && !IsUpdatingPrayerReminders;

    /// <summary>Whether the iqama lead time can currently be changed.</summary>
    public bool CanChangePrayerReminderLeadTime =>
        PrayerRemindersFeatureEnabled &&
        PrayerRemindersEnabled &&
        !IsUpdatingPrayerReminders;

    /// <summary>Whether remote push is enabled by the user (including the failed state).</summary>
    [ObservableProperty]
    private bool _pushNotificationsEnabled;

    /// <summary>The persisted remote push notification state on this device.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRetryPushRegistration))]
    private PushNotificationState _pushNotificationState = PushNotificationState.Disabled;

    /// <summary>Whether the announcements feature is enabled for the active masjid.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChangePushNotifications))]
    private bool _announcementsFeatureEnabled = true;

    /// <summary>Whether the remote push action is in progress.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChangePushNotifications))]
    [NotifyPropertyChangedFor(nameof(CanRetryPushRegistration))]
    private bool _isUpdatingPushNotifications;

    /// <summary>Whether the remote push action can currently be selected.</summary>
    public bool CanChangePushNotifications =>
        AnnouncementsFeatureEnabled && !IsUpdatingPushNotifications;

    /// <summary>Whether the registration retry button should be shown.</summary>
    public bool CanRetryPushRegistration =>
        PushNotificationState == PushNotificationState.EnabledButFailed &&
        !IsUpdatingPushNotifications;

    /// <summary>Non-blocking status for remote push permission.</summary>
    [ObservableProperty]
    private string _pushNotificationStatus = string.Empty;

    /// <summary>Display name of the currently active masjid (FR-ST-01).</summary>
    [ObservableProperty]
    private string _activeMasjidName = "None";

    /// <summary>Application version string for the About section (FR-ST-05).</summary>
    [ObservableProperty]
    private string _appVersion = string.Empty;

    /// <summary>Whether the user has opted in to anonymous usage telemetry (NFR-OB-01).</summary>
    [ObservableProperty]
    private bool _telemetryConsentEnabled;

    /// <summary>
    /// Initialises the SettingsViewModel with all required services.
    /// </summary>
    public SettingsViewModel(
        IUserPreferences preferences,
        IThemeService themeService,
        INavigationService navigation,
        IMasjidContext masjidContext,
        TelemetryService telemetry,
        IPrayerReminderSettingsService prayerReminderSettings,
        IPushNotificationSettingsService pushNotificationSettings,
        IAlertService alerts,
        IDeviceNotificationSettings deviceNotificationSettings)
    {
        _preferences = preferences;
        _themeService = themeService;
        _navigation = navigation;
        _masjidContext = masjidContext;
        _telemetry = telemetry;
        _prayerReminderSettings = prayerReminderSettings;
        _pushNotificationSettings = pushNotificationSettings;
        _alerts = alerts;
        _deviceNotificationSettings = deviceNotificationSettings;

        Title = "Settings";

        // Load persisted preferences - fall back to current themeService state for theme.
        _selectedTheme = (ThemePreference)_preferences.GetValue(ThemeKey, (int)_themeService.Current);
        _is24HourTime = _preferences.GetValue(TimeFormatKey, false);
        _prayerRemindersEnabled = _prayerReminderSettings.IsEnabled;
        _pushNotificationsEnabled = _pushNotificationSettings.State != PushNotificationState.Disabled;
        _pushNotificationState = _pushNotificationSettings.State;
        _selectedPrayerReminderLeadTimeIndex = FindLeadTimeIndex(_prayerReminderSettings.MinutesBefore);

        _activeMasjidName = _masjidContext.ActiveEntry?.Name ?? "None";
        _appVersion = $"{AppInfo.VersionString} ({AppInfo.BuildString})";
        _telemetryConsentEnabled = _telemetry.IsConsentGiven;

        _masjidContext.ActiveMasjidChanged += OnActiveMasjidChanged;
    }

    /// <summary>Navigates to the masjid-selection screen (FR-ST-01).</summary>
    [RelayCommand]
    private Task ChangeMasjidAsync() =>
        _navigation.GoToAsync(Routes.MasjidSelection);

    /// <summary>Opens the dedicated theme selection page.</summary>
    [RelayCommand]
    private Task OpenAppearanceAsync() =>
        _navigation.GoToAsync(Routes.Appearance);

    /// <summary>Opens the dedicated iqama reminder lead-time selection page.</summary>
    [RelayCommand]
    private Task OpenPrayerReminderLeadTimeAsync() =>
        CanChangePrayerReminderLeadTime
            ? _navigation.GoToAsync(Routes.PrayerReminderLeadTime)
            : Task.CompletedTask;

    /// <summary>Opens the privacy policy page (NFR-PR-02).</summary>
    [RelayCommand]
    private Task ViewPrivacyPolicyAsync() =>
        _navigation.GoToAsync(Routes.PrivacyPolicy);

    /// <summary>
    /// Cycles the theme through System → Light → Dark → System (FR-ST-03),
    /// applies it via <see cref="IThemeService"/>, and persists the choice.
    /// </summary>
    [RelayCommand]
    private void ToggleTheme()
    {
        _themeService.Toggle();
        // Setting the generated property syncs the UI and delegates persistence via
        // OnSelectedThemeChanged; Apply is idempotent so calling it again is harmless.
        SelectedTheme = _themeService.Current;
    }

    // Called by the MVVM toolkit when SelectedTheme is set (toggle command or direct binding).
    partial void OnSelectedThemeChanged(ThemePreference value)
    {
        _themeService.Apply(value);
        _preferences.SetValue(ThemeKey, (int)value);
    }

    // Called by the MVVM toolkit when Is24HourTime is toggled.
    partial void OnIs24HourTimeChanged(bool value) =>
        _preferences.SetValue(TimeFormatKey, value);

    // Called by the MVVM toolkit when TelemetryConsentEnabled is toggled.
    partial void OnTelemetryConsentEnabledChanged(bool value) =>
        _telemetry.SetConsent(value);

    public void RefreshTheme() => SelectedTheme = _themeService.Current;

    /// <summary>Refreshes reminder preferences after returning from the lead-time drill-down.</summary>
    public void RefreshPrayerReminderSettings()
    {
        PrayerRemindersEnabled = _prayerReminderSettings.IsEnabled;
        SelectedPrayerReminderLeadTimeIndex = FindLeadTimeIndex(_prayerReminderSettings.MinutesBefore);
    }

    /// <summary>Refreshes remote push consent and platform permission state.</summary>
    public async Task RefreshPushNotificationSettingsAsync(CancellationToken ct = default)
    {
        PushNotificationState = await _pushNotificationSettings.RefreshAsync(ct);
        PushNotificationsEnabled = PushNotificationState != PushNotificationState.Disabled;

        var config = (await _masjidContext.GetConfigAsync(ct: ct))?.ValueOrDefault;
        AnnouncementsFeatureEnabled = config?.FeatureFlags?.Announcements ?? false;
        PrayerRemindersFeatureEnabled = config?.FeatureFlags?.PrayerReminders ?? false;

        PushNotificationStatus = PushNotificationState == PushNotificationState.EnabledButFailed
            ? Localize("Settings_PushNotifications_Status_RegistrationFailed")
            : string.Empty;
    }

    /// <summary>Applies the requested remote announcement notification state.</summary>
    public async Task SetPushNotificationsEnabledAsync(
        bool enabled,
        CancellationToken ct = default)
    {
        if (IsUpdatingPushNotifications)
        {
            return;
        }

        IsUpdatingPushNotifications = true;
        PushNotificationStatus = string.Empty;
        try
        {
            if (!enabled)
            {
                await _pushNotificationSettings.DisableAsync(ct);
                PushNotificationState = PushNotificationState.Disabled;
                PushNotificationsEnabled = false;
            }
            else
            {
                PushNotificationState = await _pushNotificationSettings.EnableAsync(ct);
                PushNotificationsEnabled = PushNotificationState != PushNotificationState.Disabled;

                if (PushNotificationState == PushNotificationState.EnabledButFailed &&
                    !await _pushNotificationSettings.IsDeviceNotificationsEnabledAsync(ct))
                {
                    // Device notifications are off: offer to enable them in system settings.
                    await PromptToEnableNotificationsAsync();
                }
                else if (PushNotificationState == PushNotificationState.EnabledButFailed)
                {
                    PushNotificationStatus =
                        Localize("Settings_PushNotifications_Status_RegistrationFailed");
                }
            }
        }
        finally
        {
            IsUpdatingPushNotifications = false;
        }
    }

    /// <summary>
    /// Retries the remote push registration after a failure. When the POST succeeds
    /// the state becomes Enabled and the retry button area is hidden.
    /// </summary>
    [RelayCommand]
    private async Task RetryPushRegistrationAsync(CancellationToken ct = default)
    {
        if (IsUpdatingPushNotifications ||
            PushNotificationState != PushNotificationState.EnabledButFailed)
        {
            return;
        }

        IsUpdatingPushNotifications = true;
        PushNotificationStatus = string.Empty;
        try
        {
            PushNotificationState = await _pushNotificationSettings.RetryRegistrationAsync(ct);
            PushNotificationsEnabled = PushNotificationState != PushNotificationState.Disabled;
            PushNotificationStatus = PushNotificationState == PushNotificationState.EnabledButFailed
                ? Localize("Settings_PushNotifications_Status_RegistrationFailed")
                : string.Empty;
        }
        finally
        {
            IsUpdatingPushNotifications = false;
        }
    }

    /// <summary>Persists and applies the current reminder toggle and iqama lead time.</summary>
    public async Task ApplyPrayerReminderSettingsAsync(CancellationToken ct = default)
    {
        IsUpdatingPrayerReminders = true;

        try
        {
            var minutesBefore = PrayerReminderLeadTimes[SelectedPrayerReminderLeadTimeIndex];
            var result = await _prayerReminderSettings.UpdateAsync(
                PrayerRemindersEnabled,
                minutesBefore,
                ct);
            if (result == PrayerReminderUpdateResult.PermissionDenied)
            {
                PrayerRemindersEnabled = false;
                // Device notifications are off: offer to enable them in system settings.
                await PromptToEnableNotificationsAsync();
            }
        }
        finally
        {
            IsUpdatingPrayerReminders = false;
        }
    }

    /// <summary>
    /// Shows an alert explaining that notifications are disabled and opens the OS
    /// notification settings page when the user agrees. Shared by the announcements
    /// and prayer-reminder toggles so both surface the same normalized prompt.
    /// </summary>
    private async Task PromptToEnableNotificationsAsync()
    {
        var openSettings = await _alerts.DisplayAlertAsync(
            Localize("Settings_NotificationsDisabled_Title"),
            Localize("Settings_NotificationsDisabled_Message"),
            Localize("Settings_NotificationsDisabled_Accept"),
            Localize("App_Cancel"));
        if (openSettings)
        {
            await _deviceNotificationSettings.OpenAsync();
        }
    }

    private void OnActiveMasjidChanged(object? sender, EventArgs e) =>
        ActiveMasjidName = _masjidContext.ActiveEntry?.Name ?? "None";

    private static int FindLeadTimeIndex(int minutesBefore)
    {
        var index = Array.IndexOf(PrayerReminderLeadTimes, minutesBefore);
        return index >= 0 ? index : Array.IndexOf(PrayerReminderLeadTimes, 10);
    }

    private static string Localize(string key) =>
        LocalizationManager.Instance.GetString(key);
}
