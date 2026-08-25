using MasjidApp.Core.Models;
using MasjidApp.Core.PrayerTimes;
using MasjidApp.Core.Services;
using MasjidApp.Core.Widgets;
using SalahAPI.Calculations;

namespace MasjidApp.Widgets;

// TC-04, FR-WG-01, FR-WG-02, FR-WG-04: builds and pushes the widget snapshot
// whenever prayer data, the active masjid, or the calendar day changes.

/// <summary>
/// Builds a render-ready <see cref="WidgetData"/> snapshot from live prayer data
/// and writes it to the platform-specific shared container via
/// <see cref="IWidgetDataWriter"/>.  Call <see cref="RefreshAsync"/> after any
/// successful prayer-time fetch or masjid switch.
/// </summary>
public sealed class WidgetRefreshService
{
    /// <summary>Number of days of prayer data carried in each widget snapshot (FR-WG-01).</summary>
    private const int WindowDays = 7;
    private const string TimeFormatKey = "settings_24h";

    private readonly IPrayerTimesProvider _provider;
    private readonly IWidgetDataWriter _writer;
    private readonly IMasjidContext _context;
    private readonly MasjidApp.Services.IUserPreferences _preferences;

    /// <summary>Initialises a new instance of <see cref="WidgetRefreshService"/>.</summary>
    public WidgetRefreshService(
        IPrayerTimesProvider provider,
        IWidgetDataWriter writer,
        IMasjidContext context,
        MasjidApp.Services.IUserPreferences preferences)
    {
        _provider = provider;
        _writer = writer;
        _context = context;
        _preferences = preferences;
    }

    /// <summary>
    /// Fetches the next <see cref="WindowDays"/> days of prayer schedules, builds the
    /// widget snapshot, and writes it to the shared container.  Silently returns when no
    /// data is available (no active masjid, or network + cache miss) so the widget
    /// continues to render its last-known snapshot (FR-WG-05).
    /// </summary>
    /// <param name="ct">Optional cancellation token.</param>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        // On every refresh, reload the next seven days so the widget stays self-sufficient
        // for a full week without an app launch (FR-WG-01). The snapshot is overwritten
        // wholesale, so nothing accumulates across refreshes.
        var schedules = await _provider
            .GetCurrentRangeAsync(WindowDays, ct: ct)
            .ConfigureAwait(false);
        if (schedules.Count == 0)
        {
            return;
        }

        var configResult = await _context.GetConfigAsync(ct: ct).ConfigureAwait(false);
        var config = configResult?.ValueOrDefault;

        var use24HourTime = _preferences.GetValue(TimeFormatKey, false);
        var widgetData = BuildWidgetData(schedules, config, use24HourTime);
        await _writer.WriteAsync(widgetData, ct).ConfigureAwait(false);
    }

    // ── Builder ────────────────────────────────────────────────────────────────

    private static WidgetData BuildWidgetData(
        IReadOnlyList<DailyPrayerSchedule> schedules,
        MasjidConfig? config,
        bool use24HourTime)
    {
        var first = schedules[0];
        var tz = ResolveTimeZone(first.Timezone);

        var days = new List<WidgetDayData>(schedules.Count);
        foreach (var schedule in schedules)
        {
            var dayStart = schedule.Date.ToDateTime(TimeOnly.MinValue);
            var hijri = HijriDateConverter.ConvertToHijri(dayStart);
            var hijriStr = $"{hijri.Day} {MonthName(hijri.Month)} {hijri.Year}";
            var gregorianStr = dayStart.ToString("dddd, d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);

            days.Add(new WidgetDayData
            {
                DateEpochSeconds = ToEpoch(tz, schedule.Date, TimeOnly.MinValue),
                HijriDate = hijriStr,
                GregorianDate = gregorianStr,
                Prayers = BuildPrayerEntries(schedule, tz, use24HourTime),
            });
        }

        return new WidgetData
        {
            MasjidName = config?.Masjid?.Name ?? string.Empty,
            Timezone = first.Timezone,
            GeneratedAtEpochSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Days = days,
        };
    }

    private static TimeZoneInfo ResolveTimeZone(string timezone)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezone);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private static long ToEpoch(TimeZoneInfo tz, DateOnly date, TimeOnly time)
    {
        var dt = date.ToDateTime(time);
        var utc = TimeZoneInfo.ConvertTimeToUtc(dt, tz);
        return new DateTimeOffset(utc, TimeSpan.Zero).ToUnixTimeSeconds();
    }

    private static List<WidgetPrayerEntry> BuildPrayerEntries(
        DailyPrayerSchedule schedule,
        TimeZoneInfo tz,
        bool use24HourTime)
    {
        return PrayerCandidates(schedule)
            .Select(p => new WidgetPrayerEntry(
                p.Name,
                FormatTime(p.Athan, use24HourTime),
                FormatTime(p.Iqama, use24HourTime),
                ToEpoch(tz, schedule.Date, p.Athan),
                p.Iqama.HasValue ? ToEpoch(tz, schedule.Date, p.Iqama.Value) : 0))
            .ToList();
    }

    private static IEnumerable<(string Name, TimeOnly Athan, TimeOnly? Iqama)> PrayerCandidates(
        DailyPrayerSchedule schedule)
    {
        yield return ("Fajr", schedule.FajrAthan, schedule.FajrIqama);
        yield return ("Sunrise", schedule.Sunrise, null);
        yield return ("Dhuhr", schedule.DhuhrAthan, schedule.DhuhrIqama);
        yield return ("Asr", schedule.AsrAthan, schedule.AsrIqama);
        yield return ("Maghrib", schedule.MaghribAthan, schedule.MaghribIqama);
        yield return ("Isha", schedule.IshaAthan, schedule.IshaIqama);
    }

    private static string FormatTime(TimeOnly time, bool use24HourTime) =>
        time.ToString(
            use24HourTime ? "HH:mm" : "h:mm tt",
            System.Globalization.CultureInfo.InvariantCulture);

    private static string FormatTime(TimeOnly? time, bool use24HourTime) =>
        time.HasValue ? FormatTime(time.Value, use24HourTime) : string.Empty;

    private static string MonthName(int month) => month switch
    {
        1 => "Muharram",
        2 => "Safar",
        3 => "Rabi' al-Awwal",
        4 => "Rabi' al-Thani",
        5 => "Jumada al-Awwal",
        6 => "Jumada al-Thani",
        7 => "Rajab",
        8 => "Sha'ban",
        9 => "Ramadan",
        10 => "Shawwal",
        11 => "Dhul-Qa'dah",
        12 => "Dhul-Hijjah",
        _ => month.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}
