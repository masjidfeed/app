using System.Globalization;
using MasjidApp.Core.Models;

namespace MasjidApp.Core.Parking;

// API-06: pure domain logic for the parking map document. Zone types are closed
// (allowed/no-parking/event-restricted) per the /parking-config contract.

/// <summary>Parking zone type, mirroring the closed <c>type</c> values.</summary>
public enum ParkingZoneKind
{
    /// <summary>Zone is allowed (green).</summary>
    Allowed,

    /// <summary>Zone is never allowed (red).</summary>
    NoParking,

    /// <summary>Zone is restricted during an event (yellow).</summary>
    EventRestricted,
}

/// <summary>
/// Pure domain logic for the parking map document: schedule evaluation,
/// active-zone selection, and point-in-polygon zone lookup.
/// </summary>
public static class ParkingMap
{
    /// <summary>
    /// Parses the closed <c>type</c> values of a zone. Returns <c>null</c> for
    /// unknown values (new server values need an app release).
    /// </summary>
    public static ParkingZoneKind? ParseZoneKind(string? type) => type switch
    {
        "allowed" => ParkingZoneKind.Allowed,
        "no-parking" => ParkingZoneKind.NoParking,
        "event-restricted" => ParkingZoneKind.EventRestricted,
        _ => null,
    };

    /// <summary>
    /// Returns the first event whose schedule is active at the given moment
    /// (local time). When two events are active at once, the one listed first
    /// in the document wins.
    /// </summary>
    public static ParkingEvent? GetActiveEvent(ParkingConfig config, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (config.Events is null)
        {
            return null;
        }

        foreach (var parkingEvent in config.Events)
        {
            if (IsEventActive(parkingEvent, now))
            {
                return parkingEvent;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns whether the event's schedule window is active at the given moment.
    /// Times are read in the phone's local time; a window may run past midnight
    /// and the day/date is the one the window starts on.
    /// </summary>
    public static bool IsEventActive(ParkingEvent? parkingEvent, DateTimeOffset now)
    {
        if (parkingEvent?.Schedule is not { } schedule)
        {
            return false;
        }

        if (!TryParseTime(schedule.Start, out var start) ||
            !TryParseTime(schedule.End, out var end))
        {
            return false;
        }

        var local = now.LocalDateTime;
        var localTime = TimeOnly.FromDateTime(local);
        var today = DateOnly.FromDateTime(local);
        var isOvernight = start > end;

        if (string.Equals(schedule.Type, "weekly", StringComparison.OrdinalIgnoreCase))
        {
            if (schedule.Days is null || schedule.Days.Count == 0)
            {
                return false;
            }

            if (isOvernight)
            {
                // Overnight: the day the window starts on, or the day before for the tail.
                var startDay = (int)local.DayOfWeek;
                var previousDay = (startDay + 6) % 7;
                return (schedule.Days.Contains(startDay) && localTime >= start)
                    || (schedule.Days.Contains(previousDay) && localTime <= end);
            }

            return schedule.Days.Contains((int)local.DayOfWeek) &&
                start <= localTime && localTime <= end;
        }

        if (string.Equals(schedule.Type, "dates", StringComparison.OrdinalIgnoreCase))
        {
            if (schedule.Dates is null || schedule.Dates.Count == 0)
            {
                return false;
            }

            // Overnight: the date the window starts on, or the day before for the tail.
            var windowStartDay = isOvernight && localTime <= end
                ? today.AddDays(-1)
                : today;
            var dateText = windowStartDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return schedule.Dates.Contains(dateText) &&
                (isOvernight
                    ? localTime >= start || localTime <= end
                    : start <= localTime && localTime <= end);
        }

        return false;
    }

    /// <summary>
    /// Returns whether the zone is allowed at the given moment. When no event is
    /// active every zone counts as allowed for <c>event-restricted</c> zones;
    /// while an event is active, only the listed zones are checked and
    /// <c>event-restricted</c> zones count as restricted.
    /// </summary>
    public static bool IsZoneAllowed(ParkingZone zone, bool hasActiveEvent)
    {
        ArgumentNullException.ThrowIfNull(zone);

        return ParseZoneKind(zone.Type) switch
        {
            ParkingZoneKind.Allowed => true,
            ParkingZoneKind.NoParking => false,
            ParkingZoneKind.EventRestricted => !hasActiveEvent,
            _ => true,
        };
    }

    /// <summary>
    /// Returns the zones the user is shown and checked against: the zones listed
    /// by the active event when one is active, otherwise every zone.
    /// </summary>
    public static IReadOnlyList<ParkingZone> GetActiveZones(ParkingConfig config, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(config);

        var all = config.Zones ?? [];
        var activeEvent = GetActiveEvent(config, now);
        if (activeEvent?.ZoneIds is not { } zoneIds)
        {
            return all;
        }

        var filtered = all
            .Where(zone => zoneIds.Includes(zone.Id ?? string.Empty))
            .ToList();
        return filtered;
    }

    /// <summary>
    /// Returns the zone the user is currently standing in, or <c>null</c> when
    /// the point is outside every zone. The lookup is the point-in-polygon test
    /// that identifies the zone by the user's current location.
    /// </summary>
    public static ParkingZone? FindZoneAtPoint(
        ParkingConfig config,
        double latitude,
        double longitude,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(config);

        return GetActiveZones(config, now)
            .FirstOrDefault(zone => Contains(zone, latitude, longitude));
    }

    /// <summary>
    /// Returns whether the point lies inside the zone outline. The outline is
    /// closed automatically (the last point is not repeated at the end).
    /// </summary>
    public static bool Contains(ParkingZone zone, double latitude, double longitude)
    {
        ArgumentNullException.ThrowIfNull(zone);

        if (zone.Coordinates is null || zone.Coordinates.Count < 3)
        {
            return false;
        }

        // Ray casting: count crossings of the point's horizontal ray; an odd
        // count means the point is inside the polygon.
        var inside = false;
        for (int i = 0, j = zone.Coordinates.Count - 1; i < zone.Coordinates.Count; j = i++)
        {
            var a = zone.Coordinates[i];
            var b = zone.Coordinates[j];
            if ((a.Latitude > latitude) != (b.Latitude > latitude) &&
                longitude < (b.Longitude - a.Longitude) * (latitude - a.Latitude) /
                    (b.Latitude - a.Latitude) + a.Longitude)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static bool TryParseTime(string? value, out TimeOnly time)
    {
        return TimeOnly.TryParseExact(
            value,
            "HH:mm",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out time);
    }
}
