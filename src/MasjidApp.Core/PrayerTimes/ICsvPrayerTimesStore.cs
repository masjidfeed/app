namespace MasjidApp.Core.PrayerTimes;

// API-03, FR-PT-04, NFR-OF-03: durable per-day storage for the CSV prayer-times feed.

/// <summary>
/// Durable store holding one row per day of the masjid's published CSV prayer times,
/// plus the feed metadata needed for conditional refresh. Persists across app restarts
/// so the schedule renders fully offline.
/// </summary>
public interface ICsvPrayerTimesStore
{
    /// <summary>Returns metadata describing the currently stored rows.</summary>
    Task<CsvPrayerTimesFeedState> GetStateAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns stored rows falling within <paramref name="from"/>..<paramref name="toDate"/>
    /// inclusive, ordered by date. Missing days are simply absent from the result.
    /// </summary>
    Task<IReadOnlyList<CsvPrayerTimesRow>> GetRangeAsync(
        DateOnly from,
        DateOnly toDate,
        CancellationToken ct = default);

    /// <summary>
    /// Atomically replaces every stored row with <paramref name="rows"/> and records the
    /// feed metadata. Callers must only invoke this after a download has fully succeeded
    /// and parsed at least one row, so a failed refresh never destroys usable data.
    /// </summary>
    Task ReplaceAllAsync(
        IReadOnlyList<CsvPrayerTimesRow> rows,
        string feedUrl,
        string? etag,
        DateTimeOffset fetchedAt,
        CancellationToken ct = default);

    /// <summary>
    /// Records that <paramref name="feedUrl"/> was checked for changes at
    /// <paramref name="checkedAt"/> without replacing any rows. Used for
    /// <c>304 Not Modified</c> and for failed checks, to drive the daily check cadence.
    /// </summary>
    Task RecordCheckedAsync(
        string feedUrl,
        string? etag,
        DateTimeOffset checkedAt,
        CancellationToken ct = default);

    /// <summary>
    /// Discards all stored rows and metadata and adopts <paramref name="feedUrl"/> as the
    /// active feed. Used when the active masjid's CSV endpoint changes, because rows from
    /// another masjid must never be served (API-06, API-10).
    /// </summary>
    Task ResetAsync(string feedUrl, CancellationToken ct = default);
}
