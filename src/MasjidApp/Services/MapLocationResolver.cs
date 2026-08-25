using Microsoft.Extensions.Logging;

namespace MasjidApp.Services;

/// <summary>Resolves public masjid addresses through the platform geocoding service.</summary>
public sealed partial class MapLocationResolver : IMapLocationResolver
{
    private readonly ILogger<MapLocationResolver> _logger;

    /// <summary>Creates a resolver that records degraded geocoding operations.</summary>
    public MapLocationResolver(ILogger<MapLocationResolver> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<MapLocation?> ResolveAsync(
        string? address,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        try
        {
            var locations = await Geocoding.Default
                .GetLocationsAsync(address)
                .WaitAsync(cancellationToken);
            var location = locations?.FirstOrDefault();
            if (location is null)
            {
                LogNoCoordinates();
                return null;
            }

            var resolved = new MapLocation(location.Latitude, location.Longitude);
            if (!resolved.IsValid)
            {
                LogInvalidCoordinates();
                return null;
            }

            return resolved;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogResolutionFailed(exception);
            return null;
        }
    }

    [LoggerMessage(
        EventId = 2101,
        Level = LogLevel.Warning,
        Message = "No coordinates were found for the configured masjid address.")]
    private partial void LogNoCoordinates();

    [LoggerMessage(
        EventId = 2102,
        Level = LogLevel.Warning,
        Message = "Geocoding returned invalid coordinates for the configured masjid address.")]
    private partial void LogInvalidCoordinates();

    [LoggerMessage(
        EventId = 2103,
        Level = LogLevel.Warning,
        Message = "Unable to resolve the configured masjid address for map display.")]
    private partial void LogResolutionFailed(Exception exception);
}
