using MasjidApp.Core.Http;
using MasjidApp.Core.Models;

namespace MasjidApp.Core.Parking;

// API-06, NFR-RL-01: fetches the parking map document with ETag caching.

/// <summary>
/// Fetches the parking map document from <c>GET /parking-config</c> via
/// <see cref="IMasjidApiClient"/>. Conditional requests follow the ETag pattern:
/// the client echoes the stored <c>If-None-Match</c> validator, treats
/// <c>304 Not Modified</c> as cache-fresh, and stores the returned <c>ETag</c>
/// (see <c>backend/api.md</c> #etag-caching - <c>/parking-config</c> uses a weak
/// ETag with <c>max-age=60</c>).
/// </summary>
public sealed class ParkingConfigService : IParkingConfigService
{
    private readonly IMasjidApiClient _client;

    /// <summary>Initializes a new instance with the given API client.</summary>
    public ParkingConfigService(IMasjidApiClient client)
    {
        _client = client;
    }

    /// <inheritdoc/>
    public Task<ApiResult<ParkingConfig>> GetParkingConfigAsync(
        string parkingUrl,
        ApiCachePolicy cachePolicy = ApiCachePolicy.PreferCache,
        CancellationToken ct = default)
    {
        return _client.GetAsync(
            parkingUrl,
            ParkingJsonContext.Default.ParkingConfig,
            cachePolicy,
            ct);
    }
}
