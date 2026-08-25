using MasjidApp.Core.SalahApi;

namespace MasjidApp.Core.PrayerTimes;

// FR-PT-07: contract for on-device prayer-time calculation from a SalahAPI document.

/// <summary>
/// Calculates athan and iqama times for a single calendar date from a
/// <see cref="SalahApiDocument"/>.
/// </summary>
public interface IPrayerTimeCalculator
{
    /// <summary>
    /// Computes the full <see cref="DailyPrayerSchedule"/> for <paramref name="targetDate"/>,
    /// applying the masjid IANA timezone, DST overrides, and Ramadan overrides
    /// (FR-PT-06, FR-PT-07, FR-PT-08).
    /// </summary>
    /// <param name="document">Parsed SalahAPI 1.1 document containing location, calculation
    /// method, iqama rules, and seasonal overrides.</param>
    /// <param name="targetDate">The Gregorian date for which to compute prayer times.</param>
    /// <returns>A <see cref="DailyPrayerSchedule"/> populated with athan and iqama times.</returns>
    DailyPrayerSchedule Calculate(SalahApiDocument document, DateOnly targetDate);
}
