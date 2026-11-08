using FluentAssertions;
using MasjidApp.Core.Models;
using MasjidApp.Core.Parking;

namespace MasjidApp.Core.Tests.Parking;

// QA-02: pure domain logic for the parking map document - schedule windows,
// active-event selection, zone filtering, and point-in-polygon lookups.

public sealed class ParkingMapTests
{
    private const string WeeklyJson = """
        {
          "center": { "latitude": 47.7779, "longitude": -122.1906 },
          "nearRadiusMeters": 800,
          "zones": [
            {
              "id": "north-lot",
              "name": "North lot",
              "type": "no-parking",
              "message": "Please move to a permitted parking area.",
              "coordinates": [
                { "latitude": 47.7788, "longitude": -122.1913 },
                { "latitude": 47.7788, "longitude": -122.1895 },
                { "latitude": 47.7781, "longitude": -122.1895 },
                { "latitude": 47.7781, "longitude": -122.1913 }
              ]
            },
            {
              "id": "east-street",
              "name": "East street",
              "type": "event-restricted",
              "message": "Reserved for event traffic during Friday prayer.",
              "coordinates": [
                { "latitude": 47.7782, "longitude": -122.1880 },
                { "latitude": 47.7782, "longitude": -122.1872 },
                { "latitude": 47.7772, "longitude": -122.1872 },
                { "latitude": 47.7772, "longitude": -122.1880 }
              ]
            },
            {
              "id": "overflow-lot",
              "name": "Overflow lot",
              "type": "allowed",
              "coordinates": [
                { "latitude": 47.7770, "longitude": -122.1889 },
                { "latitude": 47.7770, "longitude": -122.1875 },
                { "latitude": 47.7760, "longitude": -122.1875 },
                { "latitude": 47.7760, "longitude": -122.1889 }
              ]
            }
          ],
          "events": [
            {
              "id": "jummah",
              "name": "Friday Prayer",
              "message": "Strict parking rules are in effect for Friday prayer.",
              "schedule": { "type": "weekly", "days": [5], "start": "11:30", "end": "15:30" },
              "zoneIds": ["east-street"]
            }
          ]
        }
        """;

    // Fixed Friday 2026-11-06; local offsets chosen so the schedule test is
    // deterministic regardless of the machine's own time zone.
    private static readonly DateTimeOffset FridayDuringEvent =
        new(2026, 11, 6, 13, 0, 0, TimeSpan.FromHours(-7));
    private static readonly DateTimeOffset FridayOutsideEvent =
        new(2026, 11, 6, 9, 0, 0, TimeSpan.FromHours(-7));
    private static readonly DateTimeOffset Saturday =
        new(2026, 11, 7, 13, 0, 0, TimeSpan.FromHours(-7));

    // Center of the north-lot zone outline above.
    private const double NorthLotLat = 47.7784;
    private const double NorthLotLon = -122.1904;

    private static ParkingConfig Parse(string json)
    {
        var config = System.Text.Json.JsonSerializer.Deserialize(
            json,
            ParkingJsonContext.Default.ParkingConfig);
        config.Should().NotBeNull();
        return config!;
    }

    // ── JSON parsing ────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_ParkingDocument_ZonesAndZoneIdsAreCorrect()
    {
        var config = Parse(WeeklyJson);

        config.Zones.Should().HaveCount(3);
        config.Center.Should().NotBeNull();
        config.Events.Should().HaveCount(1);
        config.Events![0].ZoneIds.Should().NotBeNull();
        config.Events[0].ZoneIds!.IsAll.Should().BeFalse();
        config.Events[0].ZoneIds!.Includes("east-street").Should().BeTrue();
        config.Events[0].ZoneIds!.Includes("north-lot").Should().BeFalse();
    }

    [Fact]
    public void Parse_ZoneIdsAll_IsRecognized()
    {
        const string json = """
            {
              "center": { "latitude": 47.7779, "longitude": -122.1906 },
              "zones": [],
              "events": [
                {
                  "id": "jummah",
                  "schedule": { "type": "weekly", "days": [5], "start": "11:30", "end": "15:30" },
                  "zoneIds": "all"
                }
              ]
            }
            """;

        var config = Parse(json);

        config.Events![0].ZoneIds!.IsAll.Should().BeTrue();
        config.Events[0].ZoneIds!.Includes("anything").Should().BeTrue();
    }

    // ── Schedule evaluation ─────────────────────────────────────────────────────

    [Fact]
    public void IsEventActive_WeeklyDuringWindow_ReturnsTrue()
    {
        var config = Parse(WeeklyJson);

        ParkingMap.IsEventActive(config.Events![0], FridayDuringEvent).Should().BeTrue();
    }

    [Fact]
    public void IsEventActive_WeeklyOutsideWindow_ReturnsFalse()
    {
        var config = Parse(WeeklyJson);

        ParkingMap.IsEventActive(config.Events![0], FridayOutsideEvent).Should().BeFalse();
        ParkingMap.IsEventActive(config.Events[0], Saturday).Should().BeFalse();
    }

    [Fact]
    public void IsEventActive_OvernightWeeklyWindow_TailAppliesToNextDay()
    {
        const string json = """
            {
              "center": { "latitude": 47.7779, "longitude": -122.1906 },
              "zones": [],
              "events": [
                {
                  "id": "night",
                  "schedule": { "type": "weekly", "days": [5], "start": "22:00", "end": "02:00" }
                }
              ]
            }
            """;
        var config = Parse(json);

        // Friday 23:00 - inside the window that starts on Friday.
        var fridayNight = new DateTimeOffset(2026, 11, 6, 23, 0, 0, TimeSpan.FromHours(-7));
        // Saturday 01:00 - inside the tail of Friday's window.
        var saturdayTail = new DateTimeOffset(2026, 11, 7, 1, 0, 0, TimeSpan.FromHours(-7));
        // Saturday 23:00 - the window starts on Friday, not Saturday.
        var saturdayNight = new DateTimeOffset(2026, 11, 7, 23, 0, 0, TimeSpan.FromHours(-7));

        ParkingMap.IsEventActive(config.Events![0], fridayNight).Should().BeTrue();
        ParkingMap.IsEventActive(config.Events[0], saturdayTail).Should().BeTrue();
        ParkingMap.IsEventActive(config.Events[0], saturdayNight).Should().BeFalse();
    }

    [Fact]
    public void IsEventActive_DatesSchedule_MatchesOnlyListedDates()
    {
        const string json = """
            {
              "center": { "latitude": 47.7779, "longitude": -122.1906 },
              "zones": [],
              "events": [
                {
                  "id": "festival",
                  "schedule": { "type": "dates", "dates": ["2026-11-06"], "start": "09:00", "end": "17:00" }
                }
              ]
            }
            """;
        var config = Parse(json);

        ParkingMap.IsEventActive(config.Events![0], FridayDuringEvent).Should().BeTrue();
        ParkingMap.IsEventActive(config.Events[0], Saturday).Should().BeFalse();
    }

    [Fact]
    public void GetActiveEvent_TwoActiveEvents_FirstWins()
    {
        const string json = """
            {
              "center": { "latitude": 47.7779, "longitude": -122.1906 },
              "zones": [],
              "events": [
                {
                  "id": "first",
                  "schedule": { "type": "dates", "dates": ["2026-11-06"], "start": "09:00", "end": "17:00" }
                },
                {
                  "id": "second",
                  "schedule": { "type": "dates", "dates": ["2026-11-06"], "start": "00:00", "end": "23:59" }
                }
              ]
            }
            """;
        var config = Parse(json);

        ParkingMap.GetActiveEvent(config, FridayDuringEvent)!.Id.Should().Be("first");
    }

    // ── Zone filtering ──────────────────────────────────────────────────────────

    [Fact]
    public void GetActiveZones_NoEvent_ReturnsEveryZone()
    {
        var config = Parse(WeeklyJson);

        ParkingMap.GetActiveZones(config, Saturday).Should().HaveCount(3);
    }

    [Fact]
    public void GetActiveZones_EventActive_ReturnsOnlyListedZones()
    {
        var config = Parse(WeeklyJson);

        var zones = ParkingMap.GetActiveZones(config, FridayDuringEvent);

        zones.Should().ContainSingle(z => z.Id == "east-street");
    }

    // ── Zone allowance ──────────────────────────────────────────────────────────

    [Fact]
    public void IsZoneAllowed_EventRestricted_CountsAsAllowedOnlyWithoutEvent()
    {
        var config = Parse(WeeklyJson);
        var zone = config.Zones!.Single(z => z.Id == "east-street");

        ParkingMap.IsZoneAllowed(zone, hasActiveEvent: false).Should().BeTrue();
        ParkingMap.IsZoneAllowed(zone, hasActiveEvent: true).Should().BeFalse();
    }

    [Fact]
    public void IsZoneAllowed_AllowedAndNoParking_AreUnaffectedByEvents()
    {
        var config = Parse(WeeklyJson);
        var allowed = config.Zones!.Single(z => z.Id == "overflow-lot");
        var noParking = config.Zones!.Single(z => z.Id == "north-lot");

        ParkingMap.IsZoneAllowed(allowed, hasActiveEvent: true).Should().BeTrue();
        ParkingMap.IsZoneAllowed(noParking, hasActiveEvent: false).Should().BeFalse();
    }

    // ── Point-in-polygon ────────────────────────────────────────────────────────

    [Fact]
    public void FindZoneAtPoint_InsideZone_ReturnsThatZone()
    {
        var config = Parse(WeeklyJson);

        var zone = ParkingMap.FindZoneAtPoint(
            config, NorthLotLat, NorthLotLon, Saturday);

        zone!.Id.Should().Be("north-lot");
    }

    [Fact]
    public void FindZoneAtPoint_OutsideZones_ReturnsNull()
    {
        var config = Parse(WeeklyJson);

        ParkingMap.FindZoneAtPoint(config, 0, 0, Saturday).Should().BeNull();
    }

    [Fact]
    public void Contains_UnknownZoneType_IsTreatedAsAllowed()
    {
        var zone = new ParkingZone
        {
            Id = "future",
            Type = "new-type",
            Coordinates =
            [
                new ParkingCoordinate(0, 0),
                new ParkingCoordinate(0, 1),
                new ParkingCoordinate(1, 1),
            ],
        };

        ParkingMap.IsZoneAllowed(zone, hasActiveEvent: true).Should().BeTrue();
        ParkingMap.ParseZoneKind(zone.Type).Should().BeNull();
    }

    [Fact]
    public void Contains_OutlineWithFewerThanThreePoints_ReturnsFalse()
    {
        var zone = new ParkingZone
        {
            Id = "degenerate",
            Coordinates =
            [
                new ParkingCoordinate(0, 0),
                new ParkingCoordinate(0, 1),
            ],
        };

        ParkingMap.Contains(zone, 0.5, 0.5).Should().BeFalse();
    }
}
