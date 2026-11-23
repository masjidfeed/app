using System.Globalization;
using MasjidApp.Core.PrayerTimes;

namespace MasjidApp.Services;

// FR-ST-03: the single entry point for rendering user-facing times. Every surface
// (prayer times, event cards, post details, notification inbox, widget snapshot)
// formats through this service so the 12/24-hour preference from Settings is
// applied consistently in one place.

/// <summary>
/// Contract for a display-time formatter that reads the user's 12/24-hour
/// preference (<c>settings_24h</c>) and applies it to every rendered time.
/// </summary>
public interface ITimeFormatService
{
    /// <summary>Whether times render on a 24-hour clock (FR-ST-03).</summary>
    bool Use24HourTime { get; }

    /// <summary>
    /// Formats a clock time, appending the meridiem designator in 12-hour mode
    /// (e.g. "5:30 PM" or "17:30").
    /// </summary>
    string Format(TimeOnly time);

    /// <summary>
    /// Formats an optional clock time with meridiem in 12-hour mode, or
    /// <paramref name="emptyText"/> when absent. <paramref name="culture"/>
    /// overrides the current culture (the home-screen widgets render with the
    /// invariant culture so their platform code can strip the meridiem).
    /// </summary>
    string Format(TimeOnly? time, string emptyText = "-", CultureInfo? culture = null);

    /// <summary>
    /// Formats a clock time without a meridiem designator in 12-hour mode
    /// (e.g. "5:30" or "17:30") for compact rows.
    /// </summary>
    string FormatCompact(TimeOnly time);

    /// <summary>
    /// Formats an optional clock time without a meridiem designator in 12-hour
    /// mode, or <paramref name="emptyText"/> when absent.
    /// </summary>
    string FormatCompact(TimeOnly? time, string emptyText = "-");

    /// <summary>Clock time of a concrete date-time (e.g. an event card).</summary>
    string FormatTimeOfDay(DateTimeOffset value);

    /// <summary>
    /// Full date and time line for event details, e.g.
    /// "Saturday, June 21, 2025 · 5:30 PM" (12-hour) or "… · 17:30" (24-hour).
    /// </summary>
    string FormatEventDateTime(DateTimeOffset value);

    /// <summary>Accessibility description variant of <see cref="FormatEventDateTime"/>.</summary>
    string FormatEventDateTimeDescription(DateTimeOffset value);

    /// <summary>Short date plus setting-aware clock time for inbox timestamps.</summary>
    string FormatNotificationTimestamp(DateTimeOffset value);
}

/// <summary>
/// Default <see cref="ITimeFormatService"/> reading the shared
/// <c>settings_24h</c> preference from <see cref="IUserPreferences"/>.
/// </summary>
public sealed class TimeFormatService : ITimeFormatService
{
    /// <summary>
    /// Preference key holding the 12/24-hour choice. Written by the Settings
    /// screen and read through this service by every display surface.
    /// </summary>
    public const string PreferenceKey = "settings_24h";

    private readonly IUserPreferences _preferences;

    /// <summary>Initializes the service with the user-preferences store.</summary>
    public TimeFormatService(IUserPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        _preferences = preferences;
    }

    /// <inheritdoc />
    public bool Use24HourTime => _preferences.GetValue(PreferenceKey, false);

    /// <inheritdoc />
    public string Format(TimeOnly time) =>
        PrayerTimeFormatter.Format(time, Use24HourTime);

    /// <inheritdoc />
    public string Format(TimeOnly? time, string emptyText = "-", CultureInfo? culture = null) =>
        time is TimeOnly value
            ? PrayerTimeFormatter.Format(value, Use24HourTime, includeMeridiem: true, culture)
            : emptyText;

    /// <inheritdoc />
    public string FormatCompact(TimeOnly time) =>
        PrayerTimeFormatter.Format(time, Use24HourTime, includeMeridiem: false);

    /// <inheritdoc />
    public string FormatCompact(TimeOnly? time, string emptyText = "-") =>
        time is TimeOnly value ? FormatCompact(value) : emptyText;

    /// <inheritdoc />
    public string FormatTimeOfDay(DateTimeOffset value) =>
        PrayerTimeFormatter.Format(value, Use24HourTime);

    /// <inheritdoc />
    public string FormatEventDateTime(DateTimeOffset value) =>
        $"{value.ToString("dddd, MMMM d, yyyy", CultureInfo.CurrentCulture)} · {FormatTimeOfDay(value)}";

    /// <inheritdoc />
    public string FormatEventDateTimeDescription(DateTimeOffset value) =>
        $"Date and time: {value.ToString("dddd, MMMM d, yyyy", CultureInfo.CurrentCulture)} at {FormatTimeOfDay(value)}";

    /// <inheritdoc />
    public string FormatNotificationTimestamp(DateTimeOffset value) =>
        $"{value.ToString("d", CultureInfo.CurrentCulture)} {FormatTimeOfDay(value)}";
}
