namespace MasjidApp.Services;

/// <summary>Coordinates for a configured masjid address.</summary>
/// <param name="Latitude">Latitude in decimal degrees.</param>
/// <param name="Longitude">Longitude in decimal degrees.</param>
public readonly record struct MapLocation(double Latitude, double Longitude)
{
    /// <summary>Returns whether both coordinates are finite and inside valid geographic bounds.</summary>
    public bool IsValid =>
        double.IsFinite(Latitude)
        && double.IsFinite(Longitude)
        && Latitude is >= -90 and <= 90
        && Longitude is >= -180 and <= 180;
}

/// <summary>Resolves a configured street address to display coordinates for a map preview.</summary>
public interface IMapLocationResolver
{
    /// <summary>Resolves an address, or returns <c>null</c> when it cannot be resolved.</summary>
    Task<MapLocation?> ResolveAsync(string? address, CancellationToken cancellationToken = default);
}
