namespace MasjidApp.Core.PrayerTimes;

// FR-PT-02, FR-WG-01: app countdowns follow the same transition rule as widgets:
// iqama when configured, otherwise athan, across the covered schedule window.

/// <summary>Stateless helper for selecting the next prayer countdown target.</summary>
public static class PrayerCountdownSelector
{
    /// <summary>
    /// Finds the nearest future athan or iqama event strictly after
    /// <paramref name="nowUtc"/> across the covered schedule window.
    /// </summary>
    public static UpcomingPrayerEvent? NextEvent(
        IEnumerable<DailyPrayerSchedule> schedules,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(schedules);

        UpcomingPrayerEvent? best = null;
        foreach (var schedule in schedules)
        {
            var timezone = ResolveTimeZone(schedule.Timezone);
            foreach (var prayer in PrayerCandidates(schedule))
            {
                Consider(prayer.Name, PrayerEventKind.Athan, prayer.Athan);
                if (prayer.Iqama is TimeOnly iqama)
                {
                    Consider(prayer.Name, PrayerEventKind.Iqama, iqama);
                }
            }

            void Consider(string name, PrayerEventKind kind, TimeOnly time)
            {
                var occursAtUtc = ToUtc(timezone, schedule.Date, time);
                if (occursAtUtc <= nowUtc || best is not null && occursAtUtc >= best.OccursAtUtc)
                {
                    return;
                }

                best = new UpcomingPrayerEvent(name, kind, occursAtUtc, occursAtUtc - nowUtc);
            }
        }

        return best;
    }

    /// <summary>
    /// Finds the next prayer transition strictly after <paramref name="nowUtc"/>.
    /// The transition time is iqama when configured, otherwise athan.
    /// </summary>
    public static PrayerCountdownTarget? NextTransition(
        IEnumerable<DailyPrayerSchedule> schedules,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(schedules);

        PrayerCountdownTarget? best = null;
        foreach (var schedule in schedules)
        {
            var timezone = ResolveTimeZone(schedule.Timezone);
            foreach (var prayer in PrayerCandidates(schedule))
            {
                var transitionTime = prayer.Iqama ?? prayer.Athan;
                var transitionUtc = ToUtc(timezone, schedule.Date, transitionTime);
                if (transitionUtc <= nowUtc)
                {
                    continue;
                }

                if (best is null || transitionUtc < best.OccursAtUtc)
                {
                    best = new PrayerCountdownTarget(
                        prayer.Name,
                        transitionUtc,
                        transitionUtc - nowUtc);
                }
            }
        }

        return best;
    }

    /// <summary>
    /// Finds the sky background active at <paramref name="nowUtc"/>. Sunrise and Maghrib
    /// take precedence for 15 minutes before and after their athan times.
    /// </summary>
    public static string? CurrentPrayerName(
        IEnumerable<DailyPrayerSchedule> schedules,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(schedules);

        var scheduleList = schedules as IReadOnlyList<DailyPrayerSchedule> ?? schedules.ToArray();
        foreach (var schedule in scheduleList)
        {
            var timezone = ResolveTimeZone(schedule.Timezone);
            var localNow = TimeZoneInfo.ConvertTime(nowUtc, timezone);
            if (schedule.Date != DateOnly.FromDateTime(localNow.DateTime))
            {
                continue;
            }

            var fajr = ToUtc(timezone, schedule.Date, schedule.FajrAthan);
            var sunrise = ToUtc(timezone, schedule.Date, schedule.Sunrise);
            var asr = ToUtc(timezone, schedule.Date, schedule.AsrAthan);
            var maghrib = ToUtc(timezone, schedule.Date, schedule.MaghribAthan);
            var skyPeriod = PrayerSkyPeriodSelector.Select(
                nowUtc.ToUnixTimeSeconds(),
                fajr.ToUnixTimeSeconds(),
                sunrise.ToUnixTimeSeconds(),
                asr.ToUnixTimeSeconds(),
                maghrib.ToUnixTimeSeconds());
            if (skyPeriod is not null)
            {
                return skyPeriod;
            }
        }

        return CurrentPrayerByAthan(scheduleList, nowUtc);
    }

    /// <summary>
    /// Finds the prayer period active at <paramref name="nowUtc"/> using athan boundaries.
    /// Unlike <see cref="CurrentPrayerName"/>, this does not apply decorative sky windows.
    /// </summary>
    public static string? CurrentPrayerPeriodName(
        IEnumerable<DailyPrayerSchedule> schedules,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(schedules);
        return CurrentPrayerByAthan(schedules, nowUtc);
    }

    private static string? CurrentPrayerByAthan(
        IEnumerable<DailyPrayerSchedule> schedules,
        DateTimeOffset nowUtc)
    {
        string? current = null;
        DateTimeOffset? currentStartUtc = null;
        (string Name, DateTimeOffset StartsAtUtc)? earliest = null;

        foreach (var schedule in schedules)
        {
            var timezone = ResolveTimeZone(schedule.Timezone);
            foreach (var prayer in PrayerCandidates(schedule))
            {
                var startsAtUtc = ToUtc(timezone, schedule.Date, prayer.Athan);
                if (earliest is null || startsAtUtc < earliest.Value.StartsAtUtc)
                {
                    earliest = (prayer.Name, startsAtUtc);
                }

                if (startsAtUtc > nowUtc)
                {
                    continue;
                }

                if (currentStartUtc is null || startsAtUtc > currentStartUtc.Value)
                {
                    current = prayer.Name;
                    currentStartUtc = startsAtUtc;
                }
            }
        }

        if (current is not null)
        {
            return current;
        }

        return earliest is not null && nowUtc < earliest.Value.StartsAtUtc ? "Isha" : null;
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

    private static DateTimeOffset ToUtc(TimeZoneInfo timezone, DateOnly date, TimeOnly time)
    {
        var local = date.ToDateTime(time);
        var utc = TimeZoneInfo.ConvertTimeToUtc(local, timezone);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }

    private static TimeZoneInfo ResolveTimeZone(string? ianaId)
    {
        if (string.IsNullOrWhiteSpace(ianaId))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(ianaId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}

/// <summary>The selected countdown target and its remaining duration.</summary>
/// <param name="Name">Display name of the selected prayer transition.</param>
/// <param name="OccursAtUtc">UTC instant when the transition occurs.</param>
/// <param name="TimeRemaining">Duration from the evaluated instant until the transition.</param>
public sealed record PrayerCountdownTarget(
    string Name,
    DateTimeOffset OccursAtUtc,
    TimeSpan TimeRemaining);

/// <summary>The type of an upcoming prayer event.</summary>
public enum PrayerEventKind
{
    /// <summary>The prayer's athan time.</summary>
    Athan,

    /// <summary>The prayer's iqama time.</summary>
    Iqama,
}

/// <summary>The nearest upcoming athan or iqama and its remaining duration.</summary>
/// <param name="Name">Display name of the prayer.</param>
/// <param name="Kind">Whether the event is athan or iqama.</param>
/// <param name="OccursAtUtc">UTC instant when the event occurs.</param>
/// <param name="TimeRemaining">Duration from the evaluated instant until the event.</param>
public sealed record UpcomingPrayerEvent(
    string Name,
    PrayerEventKind Kind,
    DateTimeOffset OccursAtUtc,
    TimeSpan TimeRemaining);
