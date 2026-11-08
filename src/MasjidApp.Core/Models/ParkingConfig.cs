using System.Text.Json.Serialization;
using MasjidApp.Core.Parking;

namespace MasjidApp.Core.Models;

// API-06: parking map document served by GET /parking-config (see backend/api.md).
// All fields are optional to support tolerant parsing of partial responses.

/// <summary>
/// Parking map document for a masjid: the mosque's location, the parking zone
/// outlines, and the schedules during which restrictions apply.
/// </summary>
public sealed record ParkingConfig
{
    /// <summary>Center of the masjid's parking area.</summary>
    [JsonPropertyName("center")]
    public ParkingCoordinate? Center { get; init; }

    /// <summary>How close to <c>center</c> counts as "near" for the parking banner.</summary>
    [JsonPropertyName("nearRadiusMeters")]
    public double? NearRadiusMeters { get; init; }

    /// <summary>Parking zone outlines.</summary>
    [JsonPropertyName("zones")]
    public List<ParkingZone>? Zones { get; init; }

    /// <summary>Events during which restrictions apply.</summary>
    [JsonPropertyName("events")]
    public List<ParkingEvent>? Events { get; init; }
}

/// <summary>A geofence zone outline.</summary>
public sealed record ParkingZone
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>
    /// Parking type of the area: <c>allowed</c> (green), <c>no-parking</c> (red),
    /// or <c>event-restricted</c> (yellow). Closed value set.
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>Optional message shown to someone standing in the zone while it is off limits.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    /// <summary>Zone outline; at least 3 points, the app closes the shape itself.</summary>
    [JsonPropertyName("coordinates")]
    public List<ParkingCoordinate>? Coordinates { get; init; }
}

/// <summary>A latitude/longitude pair.</summary>
public sealed record ParkingCoordinate(double Latitude, double Longitude);

/// <summary>
/// Event during which a set of zones is restricted. <c>zoneIds</c> is either
/// <c>"all"</c> or a list of zone ids.
/// </summary>
public sealed record ParkingEvent
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>Optional message shown while the event's restrictions are in effect.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("schedule")]
    public ParkingSchedule? Schedule { get; init; }

    [JsonPropertyName("zoneIds")]
    public ParkingZoneIds? ZoneIds { get; init; }
}

/// <summary>
/// <c>zoneIds</c> value: <c>"all"</c> or a list of zone ids. Uses a custom
/// converter because the field is either a string or an array.
/// </summary>
[JsonConverter(typeof(ParkingZoneIdsConverter))]
public sealed record ParkingZoneIds(string? SingleValue, IReadOnlyList<string>? List)
{
    /// <summary>Whether the event applies to every zone.</summary>
    public bool IsAll => SingleValue == "all";

    /// <summary>Whether the event applies to the zone with the given id.</summary>
    public bool Includes(string id) =>
        IsAll || List?.Contains(id) == true;
}

/// <summary>
/// Event schedule: <c>weekly</c> (days 0-6, Sunday..Saturday) or <c>dates</c>
/// (YYYY-MM-DD list), with a 24-hour <c>HH:mm</c> start/end window read in the
/// phone's local time. A window may run past midnight; the day/date is the one
/// the window starts on.
/// </summary>
public sealed record ParkingSchedule
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("days")]
    public List<int>? Days { get; init; }

    [JsonPropertyName("dates")]
    public List<string>? Dates { get; init; }

    [JsonPropertyName("start")]
    public string? Start { get; init; }

    [JsonPropertyName("end")]
    public string? End { get; init; }
}
