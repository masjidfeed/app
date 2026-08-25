namespace MasjidApp.Core.PrayerTimes;

// FR-PT-06, FR-PT-08: prayer dates resolve in the masjid's IANA timezone, not the device's.

/// <summary>
/// Resolves the masjid's IANA timezone and the calendar date currently in effect there.
/// </summary>
public static class MasjidTimeZone
{
    /// <summary>
    /// Returns the <see cref="TimeZoneInfo"/> for <paramref name="ianaId"/>, falling back to
    /// <see cref="TimeZoneInfo.Utc"/> when the identifier is missing or unknown to the platform.
    /// </summary>
    public static TimeZoneInfo Resolve(string? ianaId)
    {
        if (string.IsNullOrWhiteSpace(ianaId))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(ianaId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    /// <summary>
    /// Returns the current calendar date in the masjid's timezone.
    /// </summary>
    public static DateOnly Today(string? ianaId, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        var masjidNow = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), Resolve(ianaId));
        return DateOnly.FromDateTime(masjidNow.DateTime);
    }
}
