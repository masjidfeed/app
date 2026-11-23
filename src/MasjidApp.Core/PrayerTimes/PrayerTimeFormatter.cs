using System.Globalization;

namespace MasjidApp.Core.PrayerTimes;

// FR-ST-03: presentation helper for user-selected 12/24-hour display-time formatting.
// Shared by prayer times, event date-times, and notification timestamps so every
// surface follows the same clock pattern.

/// <summary>Formats display times using the user's 12/24-hour preference.</summary>
public static class PrayerTimeFormatter
{
    /// <summary>Formats a non-null time value.</summary>
    public static string Format(
        TimeOnly time,
        bool use24HourTime,
        bool includeMeridiem = true,
        CultureInfo? culture = null) =>
        time.ToString(FormatPattern(use24HourTime, includeMeridiem), culture ?? CultureInfo.CurrentCulture);

    /// <summary>Formats an optional time value, or <paramref name="emptyText"/> when it is absent.</summary>
    public static string Format(
        TimeOnly? time,
        bool use24HourTime,
        bool includeMeridiem = true,
        string emptyText = "-",
        CultureInfo? culture = null) =>
        time is TimeOnly value ? Format(value, use24HourTime, includeMeridiem, culture) : emptyText;

    /// <summary>
    /// Formats the clock time of a date-time value as stored, without timezone
    /// conversion (matching how XAML <c>StringFormat</c> renders date-times).
    /// </summary>
    public static string Format(
        DateTimeOffset time,
        bool use24HourTime,
        bool includeMeridiem = true,
        CultureInfo? culture = null) =>
        Format(new TimeOnly(time.Hour, time.Minute), use24HourTime, includeMeridiem, culture);

    private static string FormatPattern(bool use24HourTime, bool includeMeridiem) =>
        use24HourTime ? "HH:mm" : includeMeridiem ? "h:mm tt" : "h:mm";
}
