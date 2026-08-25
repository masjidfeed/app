using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MasjidApp.Core.PrayerTimes;

namespace MasjidApp.Core.Notifications;

// FR-PT-10, FR-PN-07, FR-ST-02: idempotency key for the rolling iqama reminder window so
// repeated syncs (e.g. every tab switch) do not cancel and re-post identical notifications.

/// <summary>
/// Computes a stable fingerprint of everything that affects the pending iqama reminder
/// window. When the fingerprint is unchanged, the already-scheduled notifications are
/// still correct and the whole cancel/reschedule cycle can be skipped.
/// </summary>
public static class PrayerReminderFingerprint
{
    private const string DateFormat = "yyyy-MM-dd";
    private const string TimeFormat = "HH:mm:ss";

    /// <summary>
    /// Computes the fingerprint for a reminder window.
    /// </summary>
    /// <param name="enabled">Whether iqama reminders are enabled.</param>
    /// <param name="minutesBefore">Configured lead time in minutes.</param>
    /// <param name="masjidId">Identifier of the active masjid, or <see langword="null"/> when none is selected.</param>
    /// <param name="windowStart">First date covered by the reminder window; a day rollover changes the fingerprint.</param>
    /// <param name="schedules">Daily schedules supplying the iqama times that would be scheduled.</param>
    /// <param name="use24HourTime">Whether reminder text renders times on a 24-hour clock; it is baked into the notification body.</param>
    /// <returns>A lowercase hexadecimal SHA-256 digest of the canonical window description.</returns>
    public static string Compute(
        bool enabled,
        int minutesBefore,
        string? masjidId,
        DateOnly windowStart,
        IReadOnlyList<DailyPrayerSchedule> schedules,
        bool use24HourTime = false)
    {
        ArgumentNullException.ThrowIfNull(schedules);

        var builder = new StringBuilder();
        builder.Append(enabled ? '1' : '0');
        Append(builder, minutesBefore.ToString(CultureInfo.InvariantCulture));
        Append(builder, masjidId ?? string.Empty);
        Append(builder, windowStart.ToString(DateFormat, CultureInfo.InvariantCulture));

        // The clock preference is rendered into the notification body at schedule time,
        // so changing it must refresh the pending window (FR-ST-03).
        Append(builder, use24HourTime ? "24" : "12");

        foreach (var schedule in schedules)
        {
            Append(builder, schedule.Date.ToString(DateFormat, CultureInfo.InvariantCulture));

            // The timezone converts each local iqama time into the actual alarm instant,
            // so a timezone change must invalidate the window too (FR-PT-06).
            Append(builder, schedule.Timezone);
            AppendTime(builder, schedule.FajrIqama);
            AppendTime(builder, schedule.DhuhrIqama);
            AppendTime(builder, schedule.AsrIqama);
            AppendTime(builder, schedule.MaghribIqama);
            AppendTime(builder, schedule.IshaIqama);
        }

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexStringLower(digest);
    }

    private static void Append(StringBuilder builder, string value) =>
        builder.Append('|').Append(value);

    private static void AppendTime(StringBuilder builder, TimeOnly? time) =>
        Append(builder, time?.ToString(TimeFormat, CultureInfo.InvariantCulture) ?? string.Empty);
}
