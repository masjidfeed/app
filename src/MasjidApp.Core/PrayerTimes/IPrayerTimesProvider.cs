using MasjidApp.Core.Http;

namespace MasjidApp.Core.PrayerTimes;

// FR-PT-01, FR-PT-02, FR-PT-03, NFR-OF-01: gateway for all prayer-time data consumed by the UI.

/// <summary>
/// Aggregates SalahAPI document fetch, on-device calculation, and CSV-override merging
/// into a single prayer-times facade used by the prayer-times UI and widgets.
/// </summary>
public interface IPrayerTimesProvider
{
    /// <summary>
    /// Returns the prayer schedule for today; uses cached data for offline fallback.
    /// The <see cref="ApiResult{T}"/> case indicates data freshness (FR-PT-09, NFR-OF-01).
    /// </summary>
    /// <param name="cachePolicy">Whether a fresh cached payload may bypass the network.</param>
    /// <param name="ct">Optional cancellation token.</param>
    Task<ApiResult<DailyPrayerSchedule>> GetTodayAsync(
        ApiCachePolicy cachePolicy = ApiCachePolicy.PreferCache,
        CancellationToken ct = default);

    /// <summary>
    /// Returns a contiguous range beginning on the current date in the masjid timezone.
    /// Returns an empty list when no masjid is active or the document cannot be obtained.
    /// </summary>
    /// <param name="dayCount">Number of consecutive days to return.</param>
    /// <param name="cachePolicy">Whether a fresh cached payload may bypass the network.</param>
    /// <param name="ct">Optional cancellation token.</param>
    Task<IReadOnlyList<DailyPrayerSchedule>> GetCurrentRangeAsync(
        int dayCount,
        ApiCachePolicy cachePolicy = ApiCachePolicy.PreferCache,
        CancellationToken ct = default);

    /// <summary>
    /// Returns prayer schedules for a contiguous range of dates.
    /// Returns an empty list when no masjid is active or the document cannot be obtained.
    /// </summary>
    /// <param name="from">First date in the range (inclusive).</param>
    /// <param name="toDate">Last date in the range (inclusive).</param>
    /// <param name="cachePolicy">Whether a fresh cached payload may bypass the network.</param>
    /// <param name="ct">Optional cancellation token.</param>
    Task<IReadOnlyList<DailyPrayerSchedule>> GetRangeAsync(
        DateOnly from,
        DateOnly toDate,
        ApiCachePolicy cachePolicy = ApiCachePolicy.PreferCache,
        CancellationToken ct = default);
}
