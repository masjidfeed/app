using MasjidApp.Core.Notifications;
using MasjidApp.Core.PrayerTimes;
using MasjidApp.Localization;
using Microsoft.Maui.ApplicationModel;
using Plugin.LocalNotification;
using Plugin.LocalNotification.Core.Models;

namespace MasjidApp.Notifications;

// FR-PT-10, FR-PN-07, FR-PN-05, NFR-OF-01 (C1).
// MAUI implementation backed by Plugin.LocalNotification v14.

/// <summary>
/// MAUI implementation of <see cref="IPrayerReminderScheduler"/> using Plugin.LocalNotification.
/// Registered as a singleton in MauiProgram; INotificationService is provided by UseLocalNotification().
/// </summary>
internal sealed class PrayerReminderScheduler : IPrayerReminderScheduler
{
    // Six days x ten athan/iqama slots stays under iOS's practical pending limit of 64.
    // Event reminders start at 200, so 100-159 is reserved exclusively for prayers.
    private const int NotificationIdBase = 100;
    private const int PrayerSlotsPerDay = 10;
    private const int MaximumScheduleDays = 6;
    private static readonly int[] AllPrayerIds =
        Enumerable.Range(NotificationIdBase, PrayerSlotsPerDay * MaximumScheduleDays).ToArray();

    private readonly INotificationService _notificationService;

    public PrayerReminderScheduler(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    /// <inheritdoc />
    public async Task<bool> ScheduleAsync(
        IReadOnlyList<DailyPrayerSchedule> schedules,
        PrayerNotificationPreferences prefs,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(schedules);
        ct.ThrowIfCancellationRequested();

        // Request permission with rationale; degrade gracefully if denied (FR-PN-05).
        // Scheduling runs on a background thread, but the platform permission prompt has to
        // be raised from the UI thread.
        var allowed = await _notificationService.AreNotificationsEnabled();
        if (!allowed)
        {
            allowed = await MainThread.InvokeOnMainThreadAsync<bool>(
                () => _notificationService.RequestNotificationPermission());
        }

        if (!allowed)
        {
            return false;
        }

        var templates = BuildTemplates();

        for (var dayIndex = 0; dayIndex < Math.Min(schedules.Count, MaximumScheduleDays); dayIndex++)
        {
            ct.ThrowIfCancellationRequested();
            await ScheduleDayAsync(schedules[dayIndex], prefs, templates, dayIndex);
        }

        return true;
    }

    /// <inheritdoc />
    public Task CancelAllAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _notificationService.Cancel(AllPrayerIds);
        return Task.CompletedTask;
    }

    private async Task ScheduleDayAsync(
        DailyPrayerSchedule schedule,
        PrayerNotificationPreferences prefs,
        PrayerReminderMessageTemplates templates,
        int dayIndex)
    {
        var idBase = NotificationIdBase + (dayIndex * PrayerSlotsPerDay);
        var day = new ReminderDay(schedule.Date, ResolveTimeZone(schedule.Timezone), prefs, templates);
        var athan = templates.AthanLabel;
        var iqama = templates.IqamaLabel;

        await TryScheduleAsync(day, idBase, "Fajr", athan, prefs.FajrAthan, schedule.FajrAthan);
        await TryScheduleAsync(day, idBase + 1, "Fajr", iqama,
            prefs.FajrIqama && schedule.FajrIqama.HasValue, schedule.FajrIqama.GetValueOrDefault());

        await TryScheduleAsync(day, idBase + 2, "Dhuhr", athan, prefs.DhuhrAthan, schedule.DhuhrAthan);
        await TryScheduleAsync(day, idBase + 3, "Dhuhr", iqama,
            prefs.DhuhrIqama && schedule.DhuhrIqama.HasValue, schedule.DhuhrIqama.GetValueOrDefault());

        await TryScheduleAsync(day, idBase + 4, "Asr", athan, prefs.AsrAthan, schedule.AsrAthan);
        await TryScheduleAsync(day, idBase + 5, "Asr", iqama,
            prefs.AsrIqama && schedule.AsrIqama.HasValue, schedule.AsrIqama.GetValueOrDefault());

        await TryScheduleAsync(day, idBase + 6, "Maghrib", athan, prefs.MaghribAthan, schedule.MaghribAthan);
        await TryScheduleAsync(day, idBase + 7, "Maghrib", iqama,
            prefs.MaghribIqama && schedule.MaghribIqama.HasValue, schedule.MaghribIqama.GetValueOrDefault());

        await TryScheduleAsync(day, idBase + 8, "Isha", athan, prefs.IshaAthan, schedule.IshaAthan);
        await TryScheduleAsync(day, idBase + 9, "Isha", iqama,
            prefs.IshaIqama && schedule.IshaIqama.HasValue, schedule.IshaIqama.GetValueOrDefault());
    }

    private async Task TryScheduleAsync(
        ReminderDay day,
        int id,
        string prayer,
        string eventLabel,
        bool enabled,
        TimeOnly time)
    {
        if (!enabled)
        {
            return;
        }

        var notifyTime = ToDateTimeOffset(day.Date, time, day.Zone).AddMinutes(-day.Prefs.MinutesBefore);
        if (notifyTime <= DateTimeOffset.Now)
        {
            // Scheduled time is already in the past; skip silently.
            return;
        }

        await _notificationService.Show(new NotificationRequest
        {
            NotificationId = id,
            Title = PrayerReminderMessage.BuildTitle(prayer, day.Templates),
            Description = PrayerReminderMessage.BuildBody(
                prayer,
                eventLabel,
                day.Prefs.MinutesBefore,
                time,
                day.Prefs.Use24HourTime,
                day.Templates),
            Schedule = { NotifyTime = notifyTime },
        });
    }

    /// <summary>Resolved per-day context shared by every slot scheduled for that date.</summary>
    private sealed record ReminderDay(
        DateOnly Date,
        TimeZoneInfo Zone,
        PrayerNotificationPreferences Prefs,
        PrayerReminderMessageTemplates Templates);

    private static PrayerReminderMessageTemplates BuildTemplates()
    {
        var localization = LocalizationManager.Instance;
        return new PrayerReminderMessageTemplates
        {
            TitleFormat = localization.GetString("Notification_PrayerReminder_Title"),
            LeadFormat = localization.GetString("Notification_PrayerReminder_Body_Lead"),
            AtTimeFormat = localization.GetString("Notification_PrayerReminder_Body_AtTime"),
            IqamaLabel = localization.GetString("Notification_PrayerReminder_Label_Iqama"),
            AthanLabel = localization.GetString("Notification_PrayerReminder_Label_Athan"),
            MinuteFormat = localization.GetString("Notification_Duration_Minute"),
            MinutesFormat = localization.GetString("Notification_Duration_Minutes"),
            HourFormat = localization.GetString("Notification_Duration_Hour"),
            HoursFormat = localization.GetString("Notification_Duration_Hours"),
        };
    }

    /// <summary>
    /// Converts a prayer's date + time (expressed in the masjid timezone) to a
    /// <see cref="DateTimeOffset"/> suitable for <c>NotificationRequestSchedule.NotifyTime</c>.
    /// </summary>
    private static DateTimeOffset ToDateTimeOffset(DateOnly date, TimeOnly time, TimeZoneInfo tz)
    {
        var dt = new DateTime(
            date.Year, date.Month, date.Day,
            time.Hour, time.Minute, time.Second,
            DateTimeKind.Unspecified);

        // GetUtcOffset handles DST correctly for the given date/time pair (FR-PT-06).
        var offset = tz.GetUtcOffset(dt);
        return new DateTimeOffset(dt, offset);
    }

    /// <summary>
    /// Resolves an IANA timezone ID to a <see cref="TimeZoneInfo"/>.
    /// Falls back to <see cref="TimeZoneInfo.Local"/> if the ID is absent or unknown.
    /// </summary>
    private static TimeZoneInfo ResolveTimeZone(string? ianaId)
    {
        if (string.IsNullOrWhiteSpace(ianaId))
        {
            return TimeZoneInfo.Local;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(ianaId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Local;
        }
    }
}
