namespace MasjidApp.Core.PrayerTimes;

// API-03, FR-PT-04, NFR-OF-03: persisted state of the locally stored CSV prayer-times feed.

/// <summary>
/// Metadata describing the CSV prayer-times rows currently held in local storage.
/// </summary>
/// <param name="FeedUrl">
/// The CSV endpoint the stored rows were downloaded from, or <see langword="null"/> when
/// nothing has been stored yet. Rows belonging to a different feed are never served.
/// </param>
/// <param name="ETag">Validator returned with the stored rows, used for conditional refresh.</param>
/// <param name="LastCheckedAt">When the feed was last checked for changes, successfully or not.</param>
/// <param name="LastUpdatedAt">When the stored rows were last replaced with a new download.</param>
/// <param name="MaxDate">Latest date covered by the stored rows, or <see langword="null"/> when empty.</param>
/// <param name="RowCount">Number of stored rows.</param>
public sealed record CsvPrayerTimesFeedState(
    string? FeedUrl,
    string? ETag,
    DateTimeOffset? LastCheckedAt,
    DateTimeOffset? LastUpdatedAt,
    DateOnly? MaxDate,
    int RowCount)
{
    /// <summary>State reported when no CSV rows have been stored.</summary>
    public static readonly CsvPrayerTimesFeedState Empty =
        new(null, null, null, null, null, 0);
}
