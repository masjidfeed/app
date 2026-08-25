using MasjidApp.Core.Http;

namespace MasjidApp.Core.SalahApi;

// API-01, API-02, API-03, API-08, API-11, NFR-RL-01

/// <summary>
/// Default implementation of <see cref="ISalahApiService"/>.
/// Delegates to <see cref="IMasjidApiClient"/> for resilient HTTP, conditional
/// requests (ETag/Last-Modified), and offline cache fallback.
/// </summary>
public sealed class SalahApiService : ISalahApiService
{
    private readonly IMasjidApiClient _client;

    /// <summary>
    /// Initialises the service with the shared typed HTTP client.
    /// </summary>
    /// <param name="client">Typed API client provided by the DI container.</param>
    public SalahApiService(IMasjidApiClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    /// <inheritdoc/>
    public Task<ApiResult<SalahApiDocument>> GetDocumentAsync(
        string salahApiUrl,
        ApiCachePolicy cachePolicy = ApiCachePolicy.PreferCache,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(salahApiUrl);

        // The IMasjidApiClient.BuildRequest implementation detects an absolute
        // http/https URL and uses it directly as the request URI and cache key
        // (API-10, Batch 02 convention).
        return _client.GetAsync(
            salahApiUrl,
            SalahApiJsonContext.Default.SalahApiDocument,
            cachePolicy,
            ct);
    }
}
