namespace MasjidApp.Core.PrayerTimes;

// API-03, FR-PT-04, NFR-OF-01, NFR-OF-02: CSV rows plus the coverage signal the UI needs.

/// <summary>
/// Outcome of a CSV prayer-times lookup: the stored rows for the requested range, plus
/// enough state for callers to tell published times apart from calculated estimates.
/// </summary>
/// <param name="Rows">Stored rows overlapping the requested range, ordered by date.</param>
/// <param name="IsConfigured">
/// <see langword="true"/> when the SalahAPI document declares a CSV feed. When
/// <see langword="false"/>, calculated times are the intended source and carry no degradation.
/// </param>
/// <param name="CoversRequestedRange">
/// <see langword="true"/> when every date in the requested range has a stored row. When
/// <see langword="false"/> and <paramref name="IsConfigured"/> is <see langword="true"/>,
/// the caller is falling back to calculated times and must surface that (FR-PT-04).
/// </param>
/// <param name="LastUpdatedAt">When the stored rows were last refreshed from the network.</param>
public sealed record CsvPrayerTimesResult(
    IReadOnlyList<CsvPrayerTimesRow> Rows,
    bool IsConfigured,
    bool CoversRequestedRange,
    DateTimeOffset? LastUpdatedAt)
{
    /// <summary>Result used when the document declares no CSV feed.</summary>
    public static readonly CsvPrayerTimesResult NotConfigured =
        new([], IsConfigured: false, CoversRequestedRange: true, LastUpdatedAt: null);

    /// <summary>
    /// <see langword="true"/> when a CSV feed is configured but cannot supply the whole
    /// requested range, meaning some displayed times are calculated estimates.
    /// </summary>
    public bool IsDegraded => IsConfigured && !CoversRequestedRange;
}
