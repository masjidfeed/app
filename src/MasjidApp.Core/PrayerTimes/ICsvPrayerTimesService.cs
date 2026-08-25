using MasjidApp.Core.Http;
using MasjidApp.Core.SalahApi;

namespace MasjidApp.Core.PrayerTimes;

// API-03, FR-PT-04, FR-PT-09, NFR-OF-01: offline-first CSV-backed daily prayer times.

/// <summary>
/// Serves the masjid's published daily prayer times from durable local storage,
/// refreshing that storage from the CSV feed only when needed.
/// </summary>
public interface ICsvPrayerTimesService
{
    /// <summary>
    /// Returns stored CSV prayer times for the requested range. Normal loads refresh due
    /// feeds in the background; forced refreshes wait for the network result.
    /// </summary>
    /// <param name="document">
    /// The SalahAPI document whose <c>dailyPrayerTimes.csvUrl</c> and
    /// <c>dailyPrayerTimes.csvUrlParameters</c> fields supply the CSV endpoint.
    /// </param>
    /// <param name="from">First date (inclusive) of the requested range.</param>
    /// <param name="toDate">Last date (inclusive) of the requested range.</param>
    /// <param name="cachePolicy">
    /// <see cref="ApiCachePolicy.ForceRefresh"/> bypasses the once-a-day change-check
    /// throttle and waits; normal loads return stored rows before revalidation completes.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The stored rows plus coverage state. Never throws for network or parse failures -
    /// those degrade to whatever is already stored (NFR-RL-01).
    /// </returns>
    Task<CsvPrayerTimesResult> GetCsvTimesAsync(
        SalahApiDocument document,
        DateOnly from,
        DateOnly toDate,
        ApiCachePolicy cachePolicy = ApiCachePolicy.PreferCache,
        CancellationToken ct = default);
}
