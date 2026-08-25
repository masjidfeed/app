using MasjidApp.Core.Http;

namespace MasjidApp.Core.SalahApi;

// API-01, API-02, API-03: service interface for fetching the SalahAPI 1.1 document.

/// <summary>
/// Fetches and caches the SalahAPI 1.1 document for a given masjid endpoint.
/// The implementation delegates to <see cref="IMasjidApiClient"/> to enforce
/// resilient HTTP, conditional requests, and offline fallback (API-11, API-08).
/// </summary>
public interface ISalahApiService
{
    /// <summary>
    /// Returns the parsed <see cref="SalahApiDocument"/> from the absolute
    /// <paramref name="salahApiUrl"/>, using the local cache for conditional requests
    /// and offline fallback.  Never throws - failures are encoded in the returned
    /// <see cref="ApiResult{T}"/> (NFR-RL-01).
    /// </summary>
    /// <param name="salahApiUrl">
    /// Absolute URL of the SalahAPI endpoint, e.g.
    /// <c>http://localhost:8090/salahapi</c>.  Comes from
    /// <c>MasjidConfig.Endpoints.SalahApiUrl</c>.
    /// </param>
    /// <param name="cachePolicy">Whether a fresh cached payload may bypass the network.</param>
    /// <param name="ct">Optional cancellation token.</param>
    Task<ApiResult<SalahApiDocument>> GetDocumentAsync(
        string salahApiUrl,
        ApiCachePolicy cachePolicy = ApiCachePolicy.PreferCache,
        CancellationToken ct = default);
}
