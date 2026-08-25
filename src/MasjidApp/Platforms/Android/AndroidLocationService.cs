using Android.Locations;
using Android.OS;
using MasjidApp.Services;

namespace MasjidApp.Platforms.Android;

// FR-CM-03, NFR-PR-01: Use Android's NETWORK_PROVIDER (Wi-Fi/cell towers) which only
// requires ACCESS_COARSE_LOCATION. MAUI's Geolocation.GetLocationAsync internally calls
// Permissions.EnsureGrantedAsync<Permissions.LocationWhenInUse>() which checks for
// ACCESS_FINE_LOCATION - this class bypasses that check entirely.

/// <summary>
/// Android implementation of <see cref="ILocationService"/> that uses
/// <see cref="LocationManager.NetworkProvider"/> so only <c>ACCESS_COARSE_LOCATION</c>
/// is required - no fine-location permission is declared or requested.
/// </summary>
internal sealed class AndroidLocationService : ILocationService
{
    public async Task<(double Latitude, double Longitude)?> GetApproximateLocationAsync(CancellationToken ct)
    {
        var locationManager = (LocationManager?)MauiApplication.Current
            .GetSystemService(global::Android.Content.Context.LocationService);

        if (locationManager is null)
        {
            return null;
        }

        // Providers that only need ACCESS_COARSE_LOCATION, in preference order.
        // PASSIVE_PROVIDER receives fixes already collected by other apps - it is
        // available in the emulator (where NetworkProvider is often disabled) and on
        // any device where another app has recently asked for location.
        // On API 31+ Android automatically coarsens fine-origin fixes for us.
        var coarseProviders = new[]
        {
            LocationManager.PassiveProvider,
            LocationManager.NetworkProvider,
        };

        // Try cached last-known fixes first - zero latency.
        foreach (var p in coarseProviders)
        {
            try
            {
                var last = locationManager.GetLastKnownLocation(p);
                if (last is not null)
                {
                    return (last.Latitude, last.Longitude);
                }
            }
            catch (Java.Lang.SecurityException)
            {
                // Provider's cached entry required fine permission (pre-API 31) - skip it.
            }
        }

        // No cached fix - request a single live update from the best available provider.
        var liveProvider = coarseProviders
            .FirstOrDefault(p => locationManager.IsProviderEnabled(p));

        if (liveProvider is null)
        {
            return null;
        }

        var tcs = new TaskCompletionSource<(double Lat, double Lon)?>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var listener = new SingleShotListener(tcs);

        // RequestLocationUpdates must be called on a thread with a Looper (main thread).
        await MainThread.InvokeOnMainThreadAsync(() =>
            locationManager.RequestLocationUpdates(
                liveProvider,
                0L,
                0f,
                listener,
                Looper.MainLooper!));

        using var ctr = ct.Register(() =>
        {
            locationManager.RemoveUpdates(listener);
            tcs.TrySetCanceled(ct);
        });

        try
        {
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            locationManager.RemoveUpdates(listener);
        }
    }

    private sealed class SingleShotListener : Java.Lang.Object, ILocationListener
    {
        private readonly TaskCompletionSource<(double Lat, double Lon)?> _tcs;

        public SingleShotListener(TaskCompletionSource<(double Lat, double Lon)?> tcs) => _tcs = tcs;

        public void OnLocationChanged(global::Android.Locations.Location location) =>
            _tcs.TrySetResult((location.Latitude, location.Longitude));

        public void OnProviderDisabled(string provider) => _tcs.TrySetResult(null);

        public void OnProviderEnabled(string provider) { }

        public void OnStatusChanged(string? provider, Availability status, Bundle? extras) { }
    }
}
