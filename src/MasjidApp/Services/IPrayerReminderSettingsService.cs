using MasjidApp.Core.PrayerTimes;

namespace MasjidApp.Services;

/// <summary>Outcome of applying the persisted prayer-reminder settings.</summary>
public enum PrayerReminderUpdateResult
{
    /// <summary>Reminders were disabled and pending prayer notifications were cancelled.</summary>
    Disabled,

    /// <summary>Today's available iqama reminders were scheduled.</summary>
    Scheduled,

    /// <summary>Prayer times were unavailable, so no reminders could be scheduled.</summary>
    ScheduleUnavailable,

    /// <summary>The user did not grant notification permission.</summary>
    PermissionDenied,

    /// <summary>The platform failed while updating pending notifications.</summary>
    SchedulingFailed,

    /// <summary>
    /// The pending reminder window already matches the current settings and prayer times,
    /// so scheduling was skipped.
    /// </summary>
    Unchanged,
}

/// <summary>
/// Persists the global iqama reminder settings and keeps pending local notifications in sync.
/// </summary>
public interface IPrayerReminderSettingsService
{
    /// <summary>Gets whether iqama reminders are enabled.</summary>
    bool IsEnabled { get; }

    /// <summary>Gets the configured iqama reminder lead time.</summary>
    int MinutesBefore { get; }

    /// <summary>Persists changed settings and immediately applies them to today's schedule.</summary>
    Task<PrayerReminderUpdateResult> UpdateAsync(
        bool enabled,
        int minutesBefore,
        CancellationToken ct = default);

    /// <summary>
    /// Applies persisted settings to a newly loaded rolling prayer window.
    /// Returns <see cref="PrayerReminderUpdateResult.Unchanged"/> without touching pending
    /// notifications when nothing that affects the window has changed.
    /// </summary>
    Task<PrayerReminderUpdateResult> SyncAsync(
        IReadOnlyList<DailyPrayerSchedule> schedules,
        CancellationToken ct = default);
}
