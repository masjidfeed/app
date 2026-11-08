using MasjidApp.Core.Http;
using MasjidApp.Core.Models;

namespace MasjidApp.Core.Parking;

// API-06: parking map document served by GET /parking-config.

/// <summary>
/// Fetches the active masjid's parking map document (<c>GET /parking-config</c>).
/// </summary>
public interface IParkingConfigService
{
    /// <summary>
    /// Fetches the parking map document from <paramref name="parkingUrl"/>,
    /// using the shared cache policies of the API client.
    /// </summary>
    Task<ApiResult<ParkingConfig>> GetParkingConfigAsync(
        string parkingUrl,
        ApiCachePolicy cachePolicy = ApiCachePolicy.PreferCache,
        CancellationToken ct = default);
}
