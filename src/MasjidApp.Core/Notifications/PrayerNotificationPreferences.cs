namespace MasjidApp.Core.Notifications;

// FR-PT-10, FR-PN-07 (C1): per-prayer notification preferences persisted by the user.

/// <summary>
/// Immutable record capturing which prayers should fire local notifications,
/// and how many minutes before the scheduled time to notify.
/// </summary>
public sealed record PrayerNotificationPreferences
{
    /// <summary>
    /// Creates preferences that notify before every available iqama and disable athan reminders.
    /// </summary>
    /// <param name="minutesBefore">How many minutes before iqama to notify.</param>
    /// <param name="use24HourTime">Whether reminder text renders times on a 24-hour clock.</param>
    /// <returns>Preferences configured for all five daily iqamas.</returns>
    public static PrayerNotificationPreferences AllIqamas(int minutesBefore, bool use24HourTime = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minutesBefore);

        return new PrayerNotificationPreferences
        {
            FajrAthan = false,
            FajrIqama = true,
            DhuhrIqama = true,
            AsrIqama = true,
            MaghribAthan = false,
            MaghribIqama = true,
            IshaIqama = true,
            MinutesBefore = minutesBefore,
            Use24HourTime = use24HourTime,
        };
    }

    /// <summary>Gets whether a notification fires at Fajr athan time. Default: <see langword="true"/>.</summary>
    public bool FajrAthan { get; init; } = true;

    /// <summary>Gets whether a notification fires at Fajr iqama time.</summary>
    public bool FajrIqama { get; init; }

    /// <summary>Gets whether a notification fires at Dhuhr athan time.</summary>
    public bool DhuhrAthan { get; init; }

    /// <summary>Gets whether a notification fires at Dhuhr iqama time.</summary>
    public bool DhuhrIqama { get; init; }

    /// <summary>Gets whether a notification fires at Asr athan time.</summary>
    public bool AsrAthan { get; init; }

    /// <summary>Gets whether a notification fires at Asr iqama time.</summary>
    public bool AsrIqama { get; init; }

    /// <summary>Gets whether a notification fires at Maghrib athan time. Default: <see langword="true"/>.</summary>
    public bool MaghribAthan { get; init; } = true;

    /// <summary>Gets whether a notification fires at Maghrib iqama time.</summary>
    public bool MaghribIqama { get; init; }

    /// <summary>Gets whether a notification fires at Isha athan time.</summary>
    public bool IshaAthan { get; init; }

    /// <summary>Gets whether a notification fires at Isha iqama time.</summary>
    public bool IshaIqama { get; init; }

    /// <summary>
    /// Notify this many minutes before the scheduled prayer time.
    /// Default is 0: notify at the scheduled time.
    /// </summary>
    public int MinutesBefore { get; init; }

    /// <summary>
    /// Gets whether the time embedded in the notification text uses the 24-hour clock,
    /// mirroring the user's display preference (FR-ST-03).
    /// </summary>
    public bool Use24HourTime { get; init; }
}
