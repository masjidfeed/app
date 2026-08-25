using System.Globalization;
using MasjidApp.Core.PrayerTimes;

namespace MasjidApp.Core.Notifications;

// FR-PT-10, FR-PN-07, FR-ST-03, TC-05: reminder body composition. The templates are supplied
// by the platform layer from AppResources.resx so the wording stays localizable while the
// composition rules (lead-time wording, 12/24-hour clock) stay platform-neutral and testable.

/// <summary>
/// Localized text fragments used to compose prayer reminder notification bodies.
/// Supplied by the platform layer, which owns the resource file.
/// </summary>
public sealed record PrayerReminderMessageTemplates
{
    /// <summary>Notification title: <c>{0}</c> prayer name, e.g. "Get ready for Fajr".</summary>
    public required string TitleFormat { get; init; }

    /// <summary>Body used when notifying ahead of time: <c>{0}</c> prayer, <c>{1}</c> event, <c>{2}</c> lead time, <c>{3}</c> time.</summary>
    public required string LeadFormat { get; init; }

    /// <summary>Body used when notifying at the scheduled time: <c>{0}</c> prayer, <c>{1}</c> event, <c>{2}</c> time.</summary>
    public required string AtTimeFormat { get; init; }

    /// <summary>Word describing the iqama event, e.g. "iqama".</summary>
    public required string IqamaLabel { get; init; }

    /// <summary>Word describing the athan event, e.g. "athan".</summary>
    public required string AthanLabel { get; init; }

    /// <summary>Singular minute count, e.g. "{0} minute".</summary>
    public required string MinuteFormat { get; init; }

    /// <summary>Plural minute count, e.g. "{0} minutes".</summary>
    public required string MinutesFormat { get; init; }

    /// <summary>Singular hour count, e.g. "{0} hour".</summary>
    public required string HourFormat { get; init; }

    /// <summary>Plural hour count, e.g. "{0} hours".</summary>
    public required string HoursFormat { get; init; }
}

/// <summary>
/// Builds the human-readable body of a prayer reminder notification, such as
/// "Fajr iqama in 10 minutes (5:30 AM)".
/// </summary>
public static class PrayerReminderMessage
{
    private const int MinutesPerHour = 60;

    /// <summary>Composes the notification title for a single prayer reminder, e.g. "Get ready for Fajr!".</summary>
    /// <param name="prayerName">Display name of the prayer, e.g. "Fajr".</param>
    /// <param name="templates">Localized text fragments.</param>
    /// <returns>The notification title text.</returns>
    public static string BuildTitle(string prayerName, PrayerReminderMessageTemplates templates)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prayerName);
        ArgumentNullException.ThrowIfNull(templates);

        return string.Format(CultureInfo.CurrentCulture, templates.TitleFormat, prayerName);
    }

    /// <summary>Composes the notification body for a single prayer reminder.</summary>
    /// <param name="prayerName">Display name of the prayer, e.g. "Fajr".</param>
    /// <param name="eventLabel">Localized event word, e.g. <see cref="PrayerReminderMessageTemplates.IqamaLabel"/>.</param>
    /// <param name="minutesBefore">Lead time in minutes; zero notifies at the scheduled time.</param>
    /// <param name="time">Masjid-local time of the prayer or iqama being announced.</param>
    /// <param name="use24HourTime">Whether to render <paramref name="time"/> on a 24-hour clock (FR-ST-03).</param>
    /// <param name="templates">Localized text fragments.</param>
    /// <returns>The notification body text.</returns>
    public static string BuildBody(
        string prayerName,
        string eventLabel,
        int minutesBefore,
        TimeOnly time,
        bool use24HourTime,
        PrayerReminderMessageTemplates templates)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prayerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventLabel);
        ArgumentNullException.ThrowIfNull(templates);

        var timeText = PrayerTimeFormatter.Format(time, use24HourTime);

        // A zero lead time fires at the prayer itself, so "in 0 minutes" would read wrong.
        return minutesBefore <= 0
            ? string.Format(
                CultureInfo.CurrentCulture,
                templates.AtTimeFormat,
                prayerName,
                eventLabel,
                timeText)
            : string.Format(
                CultureInfo.CurrentCulture,
                templates.LeadFormat,
                prayerName,
                eventLabel,
                FormatLeadTime(minutesBefore, templates),
                timeText);
    }

    /// <summary>Renders a lead time as "45 minutes", "1 hour", or "1 hour 30 minutes".</summary>
    public static string FormatLeadTime(int minutesBefore, PrayerReminderMessageTemplates templates)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minutesBefore);
        ArgumentNullException.ThrowIfNull(templates);

        var hours = minutesBefore / MinutesPerHour;
        var minutes = minutesBefore % MinutesPerHour;

        if (hours == 0)
        {
            return Count(minutes, templates.MinuteFormat, templates.MinutesFormat);
        }

        var hoursText = Count(hours, templates.HourFormat, templates.HoursFormat);
        return minutes == 0
            ? hoursText
            : $"{hoursText} {Count(minutes, templates.MinuteFormat, templates.MinutesFormat)}";
    }

    private static string Count(int value, string singularFormat, string pluralFormat) =>
        string.Format(
            CultureInfo.CurrentCulture,
            value == 1 ? singularFormat : pluralFormat,
            value);
}
