namespace MasjidApp.Core.PrayerTimes;

// FR-PT-04, API-03: value type for a single day's athan times sourced from CSV.

/// <summary>
/// Immutable parsed row from the CSV prayer-times feed.
/// All six athan times (Fajr, Sunrise, Dhuhr, Asr, Maghrib, Isha) plus the date, and
/// optional iqama times for the five obligatory prayers when the feed provides them.
/// </summary>
/// <param name="Date">The Gregorian date this row applies to.</param>
/// <param name="Fajr">Fajr athan time.</param>
/// <param name="Sunrise">Sunrise time.</param>
/// <param name="Dhuhr">Dhuhr athan time.</param>
/// <param name="Asr">Asr athan time.</param>
/// <param name="Maghrib">Maghrib athan time.</param>
/// <param name="Isha">Isha athan time.</param>
/// <param name="FajrIqama">Fajr iqama time, or <see langword="null"/> when absent from the feed.</param>
/// <param name="DhuhrIqama">Dhuhr iqama time, or <see langword="null"/> when absent from the feed.</param>
/// <param name="AsrIqama">Asr iqama time, or <see langword="null"/> when absent from the feed.</param>
/// <param name="MaghribIqama">Maghrib iqama time, or <see langword="null"/> when absent from the feed.</param>
/// <param name="IshaIqama">Isha iqama time, or <see langword="null"/> when absent from the feed.</param>
public sealed record CsvPrayerTimesRow(
    DateOnly Date,
    TimeOnly Fajr,
    TimeOnly Sunrise,
    TimeOnly Dhuhr,
    TimeOnly Asr,
    TimeOnly Maghrib,
    TimeOnly Isha,
    TimeOnly? FajrIqama = null,
    TimeOnly? DhuhrIqama = null,
    TimeOnly? AsrIqama = null,
    TimeOnly? MaghribIqama = null,
    TimeOnly? IshaIqama = null);
