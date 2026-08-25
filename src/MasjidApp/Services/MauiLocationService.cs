namespace MasjidApp.Services;

// Default (non-Android) implementation - delegates to MAUI's Geolocation.
// On iOS, Geolocation.GetLocationAsync with GeolocationAccuracy.Low maps to
// kCLLocationAccuracyKilometer, which does NOT require "Always" authorization.

/// <summary>
/// Location service backed by MAUI's <see cref="Geolocation"/> API.
/// Used on iOS; the Android platform folder provides a coarse-only override.
/// </summary>
internal sealed class MauiLocationService : ILocationService
{
    public async Task<(double Latitude, double Longitude)?> GetApproximateLocationAsync(CancellationToken ct)
    {
        var loc = await Geolocation.Default
            .GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Low), ct)
            .ConfigureAwait(false);

        return loc is null ? null : (loc.Latitude, loc.Longitude);
    }
}
