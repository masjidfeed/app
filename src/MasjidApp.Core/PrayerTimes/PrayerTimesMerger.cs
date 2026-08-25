namespace MasjidApp.Core.PrayerTimes;

// API-04, FR-PT-04: merge policy - CSV athan/iqama times win for dates they cover.

/// <summary>
/// Merges calculated and CSV-sourced prayer times using the override policy defined by
/// <c>API-04</c>: for dates covered by the CSV feed, CSV athan times replace calculated
/// values, and CSV iqama times replace calculated iqama when the feed supplies them
/// (otherwise the calculated iqama is preserved).
/// </summary>
public static class PrayerTimesMerger
{
    /// <summary>
    /// Returns a <see cref="DailyPrayerSchedule"/> whose athan times come from
    /// <paramref name="csvRow"/> (when non-null). Iqama times come from the CSV row when it
    /// provides them, falling back to <paramref name="calculated"/> otherwise.
    /// </summary>
    /// <param name="calculated">
    /// The schedule produced by the on-device calculation engine for the target date.
    /// </param>
    /// <param name="csvRow">
    /// The CSV row for the same date, or <see langword="null"/> when the CSV feed does
    /// not cover that date.  When null, <paramref name="calculated"/> is returned
    /// unchanged.
    /// </param>
    /// <returns>
    /// A new <see cref="DailyPrayerSchedule"/> (or the same instance when
    /// <paramref name="csvRow"/> is null).
    /// </returns>
    public static DailyPrayerSchedule Merge(DailyPrayerSchedule calculated, CsvPrayerTimesRow? csvRow)
    {
        if (csvRow is null)
        {
            return calculated;
        }

        return calculated with
        {
            FajrAthan = csvRow.Fajr,
            Sunrise = csvRow.Sunrise,
            DhuhrAthan = csvRow.Dhuhr,
            AsrAthan = csvRow.Asr,
            MaghribAthan = csvRow.Maghrib,
            IshaAthan = csvRow.Isha,
            FajrIqama = csvRow.FajrIqama ?? calculated.FajrIqama,
            DhuhrIqama = csvRow.DhuhrIqama ?? calculated.DhuhrIqama,
            AsrIqama = csvRow.AsrIqama ?? calculated.AsrIqama,
            MaghribIqama = csvRow.MaghribIqama ?? calculated.MaghribIqama,
            IshaIqama = csvRow.IshaIqama ?? calculated.IshaIqama,
        };
    }
}
