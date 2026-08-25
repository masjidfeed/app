using MauiPermissions = Microsoft.Maui.ApplicationModel.Permissions;

namespace MasjidApp.PlatformPermissions;

// FR-CM-03, NFR-PR-01: Qibla only needs an approximate position. The built-in
// Permissions.LocationWhenInUse maps to ACCESS_FINE_LOCATION on Android, which would
// force us to declare fine-location access we do not need. This subclass requests only
// ACCESS_COARSE_LOCATION so the app collects the minimum location data necessary.

/// <summary>
/// Location-when-in-use permission that requires only coarse (approximate) location on
/// Android, satisfying the Qibla bearing calculation without fine-location access.
/// On other platforms it behaves identically to <see cref="MauiPermissions.LocationWhenInUse"/>.
/// </summary>
public sealed class CoarseLocationWhenInUse : MauiPermissions.LocationWhenInUse
{
#if ANDROID
    /// <inheritdoc/>
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
        new (string, bool)[]
        {
            (global::Android.Manifest.Permission.AccessCoarseLocation, true),
        };
#endif
}
