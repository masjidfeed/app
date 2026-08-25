using MasjidApp.Core.PrayerTimes;

namespace MasjidApp.Core.Widgets;

// FR-WG-01, FR-WG-06: pure selection logic shared by the native widget consumers so
// Android and iOS derive the current day, current prayer, next prayer, and staleness identically.

/// <summary>
/// Stateless helpers that pick the relevant slice of a multi-day <see cref="WidgetData"/>
/// snapshot for a given instant. Kept platform-agnostic and unit-tested so the Android
/// (C#) and iOS (Swift, which mirrors this logic) widgets stay in parity.
/// </summary>
public static class WidgetDaySelector
{
    private const long SecondsPerDay = 86_400L;

    /// <summary>
    /// Unix epoch seconds at which the covered window ends: the start of the last day plus
    /// 24 hours. Returns zero when there are no days. Once "now" reaches this instant the
    /// snapshot no longer covers the current day and is considered stale.
    /// </summary>
    public static long CoverageEndEpochSeconds(WidgetData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        long max = long.MinValue;
        var any = false;
        foreach (var day in data.Days)
        {
            if (day.DateEpochSeconds > max)
            {
                max = day.DateEpochSeconds;
            }

            any = true;
        }

        return any ? max + SecondsPerDay : 0;
    }

    /// <summary>
    /// True when <paramref name="nowEpochSeconds"/> has run past the covered window, i.e.
    /// the snapshot no longer contains a schedule for the current day (FR-WG-01).
    /// </summary>
    public static bool IsStale(WidgetData data, long nowEpochSeconds)
    {
        var end = CoverageEndEpochSeconds(data);
        return end > 0 && nowEpochSeconds >= end;
    }

    /// <summary>
    /// The day containing <paramref name="nowEpochSeconds"/> - the latest day whose start is
    /// at or before now. Falls back to the earliest day when now precedes the window, and to
    /// the latest day when now is past it, so the widget always has a day to render.
    /// </summary>
    public static WidgetDayData? CurrentDay(WidgetData data, long nowEpochSeconds)
    {
        ArgumentNullException.ThrowIfNull(data);

        WidgetDayData? earliest = null;
        WidgetDayData? latest = null;
        WidgetDayData? match = null;
        foreach (var day in data.Days)
        {
            if (earliest is null || day.DateEpochSeconds < earliest.DateEpochSeconds)
            {
                earliest = day;
            }

            if (latest is null || day.DateEpochSeconds > latest.DateEpochSeconds)
            {
                latest = day;
            }

            if (day.DateEpochSeconds <= nowEpochSeconds &&
                (match is null || day.DateEpochSeconds > match.DateEpochSeconds))
            {
                match = day;
            }
        }

        if (match is not null)
        {
            return match;
        }

        // now is before the first day → show the first day; covered by IsStale otherwise.
        return earliest ?? latest;
    }

    /// <summary>
    /// The next athan or iqama transition strictly after <paramref name="nowEpochSeconds"/>
    /// across the whole window, or <c>null</c> when none remain. Athan is selected before
    /// the prayer; iqama is selected between athan and iqama (FR-WG-01).
    /// </summary>
    public static WidgetCountdownTarget? NextCountdownTarget(WidgetData data, long nowEpochSeconds)
    {
        ArgumentNullException.ThrowIfNull(data);

        WidgetCountdownTarget? best = null;
        var bestEpoch = long.MaxValue;
        foreach (var day in data.Days)
        {
            foreach (var prayer in day.Prayers)
            {
                Consider(prayer.AthanEpochSeconds, WidgetTransitionKind.Athan);
                Consider(prayer.IqamaEpochSeconds, WidgetTransitionKind.Iqama);

                void Consider(long epoch, WidgetTransitionKind kind)
                {
                    if (epoch > nowEpochSeconds && epoch < bestEpoch)
                    {
                        bestEpoch = epoch;
                        best = new WidgetCountdownTarget(prayer, epoch, kind);
                    }
                }
            }
        }

        return best;
    }

    /// <summary>
    /// Name of the prayer associated with the next athan or iqama transition.
    /// </summary>
    public static string? NextPrayerName(WidgetData data, long nowEpochSeconds) =>
        NextCountdownTarget(data, nowEpochSeconds)?.Prayer.Name;

    /// <summary>
    /// Remaining fraction of the interval leading to <paramref name="target"/>, clamped
    /// from one at the prior transition to zero at the target. Uses a one-hour interval
    /// when the snapshot has no prior transition.
    /// </summary>
    public static double RemainingProgress(
        WidgetData data,
        WidgetCountdownTarget target,
        long nowEpochSeconds)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(target);

        long? previous = null;
        foreach (var prayer in data.Days.SelectMany(static day => day.Prayers))
        {
            Consider(prayer.AthanEpochSeconds);
            Consider(prayer.IqamaEpochSeconds);

            void Consider(long epoch)
            {
                if (epoch > 0 && epoch < target.EpochSeconds && (previous is null || epoch > previous))
                {
                    previous = epoch;
                }
            }
        }

        var startEpoch = previous ?? target.EpochSeconds - 3600;
        var total = Math.Max(1, target.EpochSeconds - startEpoch);
        var remaining = Math.Clamp(target.EpochSeconds - nowEpochSeconds, 0, total);
        return remaining / (double)total;
    }

    /// <summary>
    /// Name of the sky background active at <paramref name="nowEpochSeconds"/>. Sunrise and
    /// Maghrib take precedence for 15 minutes before and after their times; the remaining
    /// periods are Fajr, Dhuhr, Asr, and Isha.
    /// </summary>
    public static string? CurrentPrayerName(WidgetData data, long nowEpochSeconds)
    {
        ArgumentNullException.ThrowIfNull(data);

        var day = CurrentDay(data, nowEpochSeconds);
        if (day is null)
        {
            return null;
        }

        var fajr = AthanEpoch(day, "Fajr");
        var sunrise = AthanEpoch(day, "Sunrise");
        var asr = AthanEpoch(day, "Asr");
        var maghrib = AthanEpoch(day, "Maghrib");
        return PrayerSkyPeriodSelector.Select(nowEpochSeconds, fajr, sunrise, asr, maghrib) ??
            CurrentPrayerByAthan(data, nowEpochSeconds);
    }

    /// <summary>
    /// Name of the prayer period active at <paramref name="nowEpochSeconds"/> using athan
    /// boundaries, without the decorative Sunrise and Maghrib sky windows.
    /// </summary>
    public static string? CurrentPrayerPeriodName(WidgetData data, long nowEpochSeconds)
    {
        ArgumentNullException.ThrowIfNull(data);
        return CurrentPrayerByAthan(data, nowEpochSeconds);
    }

    private static long AthanEpoch(WidgetDayData day, string prayerName) =>
        day.Prayers.FirstOrDefault(prayer =>
            string.Equals(prayer.Name, prayerName, StringComparison.OrdinalIgnoreCase))?.AthanEpochSeconds ?? 0;

    private static string? CurrentPrayerByAthan(WidgetData data, long nowEpochSeconds)
    {
        var current = data.Days
            .SelectMany(static day => day.Prayers)
            .Where(static prayer => prayer.AthanEpochSeconds > 0)
            .OrderBy(static prayer => prayer.AthanEpochSeconds)
            .LastOrDefault(prayer => prayer.AthanEpochSeconds <= nowEpochSeconds);

        return current?.Name ?? (data.Days.Count > 0 ? "Isha" : null);
    }
}

/// <summary>The event kind targeted by a widget countdown.</summary>
public enum WidgetTransitionKind
{
    /// <summary>The prayer's athan time.</summary>
    Athan,

    /// <summary>The prayer's iqama time.</summary>
    Iqama,
}

/// <summary>A render-ready target for a native widget countdown.</summary>
/// <param name="Prayer">Prayer associated with the transition.</param>
/// <param name="EpochSeconds">Transition time as Unix epoch seconds.</param>
/// <param name="Kind">Whether the transition is athan or iqama.</param>
public sealed record WidgetCountdownTarget(
    WidgetPrayerEntry Prayer,
    long EpochSeconds,
    WidgetTransitionKind Kind);
