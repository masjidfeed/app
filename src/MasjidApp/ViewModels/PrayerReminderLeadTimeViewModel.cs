using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasjidApp.Localization;
using MasjidApp.Services;

namespace MasjidApp.ViewModels;

/// <summary>An iqama reminder lead-time choice shown in the notification drill-down.</summary>
public sealed partial class PrayerReminderLeadTimeOption : ObservableObject
{
    public PrayerReminderLeadTimeOption(
        int minutesBefore,
        string displayName,
        bool isSelected,
        bool showSeparator = true)
    {
        MinutesBefore = minutesBefore;
        DisplayName = displayName;
        ShowSeparator = showSeparator;
        _isSelected = isSelected;
    }

    public int MinutesBefore { get; }

    public string DisplayName { get; }

    public bool ShowSeparator { get; }

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>Iqama reminder lead-time selection shown from Settings notifications.</summary>
public sealed partial class PrayerReminderLeadTimeViewModel : ViewModelBase
{
    private readonly IPrayerReminderSettingsService _prayerReminderSettings;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSelectLeadTime))]
    private bool _isUpdating;

    /// <summary>Failure-only status for the most recent scheduling attempt.</summary>
    [ObservableProperty]
    private string _status = string.Empty;

    public PrayerReminderLeadTimeViewModel(IPrayerReminderSettingsService prayerReminderSettings)
    {
        _prayerReminderSettings = prayerReminderSettings;
        Title = Localize("Settings_PrayerReminder_LeadTime_Label");

        var selectedMinutes = prayerReminderSettings.MinutesBefore;
        Options =
        [
            CreateOption(0, "Settings_PrayerReminder_LeadTime_AtIqama", selectedMinutes),
            CreateOption(5, "Settings_PrayerReminder_LeadTime_5", selectedMinutes),
            CreateOption(10, "Settings_PrayerReminder_LeadTime_10", selectedMinutes),
            CreateOption(15, "Settings_PrayerReminder_LeadTime_15", selectedMinutes),
            CreateOption(20, "Settings_PrayerReminder_LeadTime_20", selectedMinutes),
            CreateOption(30, "Settings_PrayerReminder_LeadTime_30", selectedMinutes),
            CreateOption(45, "Settings_PrayerReminder_LeadTime_45", selectedMinutes),
            CreateOption(60, "Settings_PrayerReminder_LeadTime_60", selectedMinutes, showSeparator: false),
        ];
    }

    public IReadOnlyList<PrayerReminderLeadTimeOption> Options { get; }

    public bool CanSelectLeadTime => _prayerReminderSettings.IsEnabled && !IsUpdating;

    [RelayCommand]
    private async Task SelectLeadTimeAsync(
        PrayerReminderLeadTimeOption? option,
        CancellationToken ct)
    {
        if (option is null || option.IsSelected || !CanSelectLeadTime)
        {
            return;
        }

        IsUpdating = true;
        Status = string.Empty;

        try
        {
            var result = await _prayerReminderSettings.UpdateAsync(
                enabled: true,
                option.MinutesBefore,
                ct);

            foreach (var candidate in Options)
            {
                candidate.IsSelected = candidate.MinutesBefore == option.MinutesBefore;
            }

            Status = result switch
            {
                PrayerReminderUpdateResult.ScheduleUnavailable =>
                    Localize("Settings_PrayerReminder_Status_Unavailable"),
                PrayerReminderUpdateResult.PermissionDenied =>
                    Localize("Settings_PrayerReminder_Status_Denied"),
                PrayerReminderUpdateResult.SchedulingFailed =>
                    Localize("Settings_PrayerReminder_Status_Failed"),
                _ => string.Empty,
            };
        }
        finally
        {
            IsUpdating = false;
        }
    }

    private static PrayerReminderLeadTimeOption CreateOption(
        int minutesBefore,
        string resourceKey,
        int selectedMinutes,
        bool showSeparator = true) =>
        new(minutesBefore, Localize(resourceKey), minutesBefore == selectedMinutes, showSeparator);

    private static string Localize(string key) =>
        LocalizationManager.Instance.GetString(key);
}
