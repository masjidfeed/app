using System.Globalization;

namespace MasjidApp.Core.PrayerTimes;

// FR-ST-03: presentation helper for user-selected 12/24-hour prayer time formatting.

/// <summary>Formats masjid-local prayer times for display using the user's 12/24-hour preference.</summary>
public static class PrayerTimeFormatter
{
    /// <summary>Formats a non-null time value.</summary>
    public static string Format(TimeOnly time, bool use24HourTime, bool includeMeridiem = true) =>
        time.ToString(FormatPattern(use24HourTime, includeMeridiem), CultureInfo.CurrentCulture);

    /// <summary>Formats an optional time value, or <paramref name="emptyText"/> when it is absent.</summary>
    public static string Format(TimeOnly? time, bool use24HourTime, bool includeMeridiem = true, string emptyText = "-") =>
        time is TimeOnly value ? Format(value, use24HourTime, includeMeridiem) : emptyText;

    private static string FormatPattern(bool use24HourTime, bool includeMeridiem) =>
        use24HourTime ? "HH:mm" : includeMeridiem ? "h:mm tt" : "h:mm";
}
