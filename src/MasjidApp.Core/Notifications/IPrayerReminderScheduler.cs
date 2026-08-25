using MasjidApp.Core.PrayerTimes;

namespace MasjidApp.Core.Notifications;

// FR-PT-10, FR-PN-07, FR-PN-05, NFR-OF-01 (C1).

/// <summary>
/// Schedules and cancels local athan/iqama reminder notifications for a rolling prayer window.
/// Platform implementations live in the MAUI head project; this interface is
/// declared here so ViewModels and other Core services can consume it via DI.
/// </summary>
public interface IPrayerReminderScheduler
{
    /// <summary>
    /// Schedules local notifications for the given prayer schedules
    /// based on the user's notification preferences.
    /// Requests permission first; degrades gracefully if permission is denied (FR-PN-05).
    /// </summary>
    /// <param name="schedules">Daily prayer schedules supplying athan/iqama times.</param>
    /// <param name="prefs">Per-prayer toggle flags and lead-time configuration.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see langword="true"/> when notification permission is available; otherwise <see langword="false"/>.</returns>
    Task<bool> ScheduleAsync(
        IReadOnlyList<DailyPrayerSchedule> schedules,
        PrayerNotificationPreferences prefs,
        CancellationToken ct = default);

    /// <summary>Cancels all pending prayer notifications in the rolling window.</summary>
    /// <param name="ct">Cancellation token.</param>
    Task CancelAllAsync(CancellationToken ct = default);
}
