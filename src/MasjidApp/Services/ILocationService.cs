namespace MasjidApp.Services;

// FR-CM-03, NFR-PR-01: Abstraction over device location so the Android implementation
// can use NETWORK_PROVIDER (coarse only) without going through MAUI's Geolocation API,
// which internally checks for ACCESS_FINE_LOCATION even when accuracy is set to Low.

/// <summary>
/// Provides a single approximate device location, using only coarse-location access.
/// </summary>
public interface ILocationService
{
    /// <summary>
    /// Returns the device's approximate latitude and longitude, or <c>null</c> when the
    /// location cannot be determined (provider disabled, no cached fix, etc.).
    /// </summary>
    Task<(double Latitude, double Longitude)?> GetApproximateLocationAsync(CancellationToken ct);
}
