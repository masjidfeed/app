using System.Text.Json;
using FluentAssertions;
using MasjidApp.Core.SalahApi;

namespace MasjidApp.Core.Tests.SalahApi;

// QA-02, QA-03: verifies SalahApiDocument parsing against the mock/salahapi.json shape
// (API-07, NFR-RL-01) - tolerant of missing and unknown fields.

public sealed class SalahApiDocumentTests
{
    // ── Mock document (mirrors mock/salahapi.json) ─────────────────────────────

    private const string MockSalahApiJson = """
        {
          "salahapi": "1.1",
          "info": {
            "title": "Islamic Center of Bothell Prayer Times",
            "description": "Sample SalahAPI 1.1 document for local development (DV-04).",
            "version": "1.0.0",
            "contact": {
              "name": "Islamic Center of Bothell",
              "email": "info@bothellmosque.org"
            }
          },
          "location": {
            "latitude": 47.7623,
            "longitude": -122.2054,
            "elevation": 30,
            "timezone": "America/Los_Angeles",
            "dateFormat": "YYYY-MM-DD",
            "timeFormat": "h:mm A"
          },
          "calculationMethod": {
            "name": "ISNA",
            "asrCalculationMethod": "Standard",
            "highLatitudeAdjustment": "AngleBased",
            "iqamaCalculationRules": {
              "changeOn": "friday",
              "fajr": {
                "afterAthanMinutes": 20,
                "roundMinutes": 5,
                "overrides": [
                  { "condition": "ramadan", "time": { "afterAthanMinutes": 25 } }
                ]
              },
              "dhuhr": { "static": "13:30" },
              "asr": { "afterAthanMinutes": 15, "roundMinutes": 5 },
              "maghrib": { "afterAthanMinutes": 5 },
              "isha": {
                "afterAthanMinutes": 15,
                "roundMinutes": 5,
                "earliest": "20:00",
                "latest": "22:30",
                "overrides": [
                  { "condition": "daylightSavingsTime", "time": { "afterAthanMinutes": 15, "latest": "23:00" } },
                  { "condition": "ramadan", "time": { "static": "22:00" } }
                ]
              }
            },
            "jumuahRules": [
              {
                "name": "First Jumuah",
                "time": { "static": "13:00" },
                "location": { "name": "Main Prayer Hall", "address": "10921 NE 191st St, Bothell, WA 98011, USA" }
              },
              {
                "name": "Second Jumuah",
                "time": { "static": "14:15" },
                "location": { "name": "Community Hall", "address": "10921 NE 191st St, Bothell, WA 98011, USA" }
              }
            ]
          },
          "dailyPrayerTimes": {
            "csvUrl": "http://localhost:8090/prayer-times.csv",
            "csvUrlParameters": {
              "fromDate": "2026-06-01",
              "toDate": "2026-06-30",
              "static": { "format": "24h" }
            },
            "dateFormat": "YYYY-MM-DD",
            "timeFormat": "h:mm A"
          }
        }
        """;

    // ── Full mock document ─────────────────────────────────────────────────────

    [Fact]
    public void Deserialize_MockDocument_ParsesVersion()
    {
        var doc = Deserialize(MockSalahApiJson);

        doc.Should().NotBeNull();
        doc!.Version.Should().Be("1.1");
    }

    [Fact]
    public void Deserialize_MockDocument_ParsesLocation()
    {
        var doc = Deserialize(MockSalahApiJson);

        doc!.Location.Should().NotBeNull();
        doc.Location!.Timezone.Should().Be("America/Los_Angeles");
        doc.Location.Latitude.Should().BeApproximately(47.7623, 0.0001);
        doc.Location.Longitude.Should().BeApproximately(-122.2054, 0.0001);
        doc.Location.DateFormat.Should().Be("YYYY-MM-DD");
        doc.Location.TimeFormat.Should().Be("h:mm A");
        doc.Location.Elevation.Should().Be(30);
    }

    [Fact]
    public void Deserialize_MockDocument_ParsesInfo()
    {
        var doc = Deserialize(MockSalahApiJson);

        doc!.Info.Should().NotBeNull();
        doc.Info!.Title.Should().Be("Islamic Center of Bothell Prayer Times");
        doc.Info.Version.Should().Be("1.0.0");
        doc.Info.Contact!.Email.Should().Be("info@bothellmosque.org");
    }

    [Fact]
    public void Deserialize_MockDocument_ParsesCalculationMethod()
    {
        var doc = Deserialize(MockSalahApiJson);

        doc!.CalculationMethod.Should().NotBeNull();
        doc.CalculationMethod!.Name.Should().Be("ISNA");
        doc.CalculationMethod.AsrCalculationMethod.Should().Be("Standard");
        doc.CalculationMethod.HighLatitudeAdjustment.Should().Be("AngleBased");
    }

    [Fact]
    public void Deserialize_MockDocument_ParsesIqamaCalculationRules()
    {
        var doc = Deserialize(MockSalahApiJson);

        var rules = doc!.CalculationMethod!.IqamaCalculationRules;
        rules.Should().NotBeNull();

        rules!.Fajr.Should().NotBeNull();
        rules.Fajr!.AfterAthanMinutes.Should().Be(20);
        rules.Fajr.RoundMinutes.Should().Be(5);
        rules.Fajr.Static.Should().BeNull();

        rules.Dhuhr.Should().NotBeNull();
        rules.Dhuhr!.Static.Should().Be("13:30");
        rules.Dhuhr.AfterAthanMinutes.Should().BeNull();

        rules.Asr.Should().NotBeNull();
        rules.Asr!.AfterAthanMinutes.Should().Be(15);

        rules.Maghrib.Should().NotBeNull();
        rules.Maghrib!.AfterAthanMinutes.Should().Be(5);

        rules.Isha.Should().NotBeNull();
        rules.Isha!.AfterAthanMinutes.Should().Be(15);
        rules.Isha.RoundMinutes.Should().Be(5);
        rules.Isha.Earliest.Should().Be("20:00");
        rules.Isha.Latest.Should().Be("22:30");
    }

    [Fact]
    public void Deserialize_MockDocument_ParsesJumuahRules()
    {
        var doc = Deserialize(MockSalahApiJson);

        var rules = doc!.CalculationMethod!.JumuahRules!;
        rules.Should().HaveCount(2);

        var first = rules[0];
        first.Name.Should().Be("First Jumuah");
        first.Time!.Static.Should().Be("13:00");
        first.Location.Should().NotBeNull();
        first.Location!.Name.Should().Be("Main Prayer Hall");

        var second = rules[1];
        second.Name.Should().Be("Second Jumuah");
        second.Time!.Static.Should().Be("14:15");
        second.Location!.Name.Should().Be("Community Hall");
    }

    [Fact]
    public void Deserialize_JumuahPrayerCalculationRule_ParsesStaticTime()
    {
        const string json = """
          {
            "salahapi": "1.1",
            "calculationMethod": {
              "jumuahRules": [
                { "name": "Jumuah 1", "time": { "static": "13:30" } },
                { "name": "Jumuah 2", "time": { "static": "14:30" } }
              ]
            }
          }
          """;

        var doc = Deserialize(json);

        var rules = doc!.CalculationMethod!.JumuahRules;
        rules.Should().HaveCount(2);
        rules![0].Time!.Static.Should().Be("13:30");
        rules[1].Time!.Static.Should().Be("14:30");
    }

    [Fact]
    public void Deserialize_ProductionCalculationRules_AcceptsNumericStrings()
    {
        const string json = """
            {
              "calculationMethod": {
                "iqamaCalculationRules": {
                  "fajr": { "roundMinutes": "15" },
                  "isha": { "afterAthanMinutes": "10", "roundMinutes": "1" }
                },
                "jumuahRules": [
                  { "name": "Jumuah 1", "time": { "static": "13:30" } }
                ]
              }
            }
            """;

        var method = Deserialize(json)!.CalculationMethod!;

        method.IqamaCalculationRules!.Fajr!.RoundMinutes.Should().Be(15);
        method.IqamaCalculationRules.Isha!.AfterAthanMinutes.Should().Be(10);
        method.IqamaCalculationRules.Isha.RoundMinutes.Should().Be(1);
        method.JumuahRules.Should().ContainSingle();
    }

    [Fact]
    public void Deserialize_NonStaticJumuahRule_PreservesCalculationRule()
    {
        const string json = """
          {
            "calculationMethod": {
              "jumuahRules": [
                { "name": "Jumuah", "time": { "afterAthanMinutes": 30 } }
              ]
            }
          }
          """;

        var doc = Deserialize(json);

        var rule = doc!.CalculationMethod!.JumuahRules.Should().ContainSingle().Subject;
        rule.Time!.Static.Should().BeNull();
        rule.Time.AfterAthanMinutes.Should().Be(30);
    }

    [Fact]
    public void Deserialize_MockDocument_ParsesRuleOverrides()
    {
        var doc = Deserialize(MockSalahApiJson);

        var rules = doc!.CalculationMethod!.IqamaCalculationRules!;
        rules.ChangeOn.Should().Be("friday");
        rules.Fajr!.Overrides.Should().ContainSingle()
          .Which.Time!.AfterAthanMinutes.Should().Be(25);
        rules.Isha!.Overrides.Should().HaveCount(2);
        rules.Isha.Overrides![0].Condition.Should().Be("daylightSavingsTime");
        rules.Isha.Overrides[0].Time!.Latest.Should().Be("23:00");
        rules.Isha.Overrides[1].Condition.Should().Be("ramadan");
        rules.Isha.Overrides[1].Time!.Static.Should().Be("22:00");
    }

    [Fact]
    public void Deserialize_MockDocument_ParsesDailyPrayerTimes()
    {
        var doc = Deserialize(MockSalahApiJson);

        doc!.DailyPrayerTimes.Should().NotBeNull();
        doc.DailyPrayerTimes!.CsvUrl.Should().Be("http://localhost:8090/prayer-times.csv");
        doc.DailyPrayerTimes.CsvUrlParameters.Should().NotBeNull();
        doc.DailyPrayerTimes.CsvUrlParameters!.Value.ValueKind.Should().Be(JsonValueKind.Object);
        doc.DailyPrayerTimes.DateFormat.Should().Be("YYYY-MM-DD");
        doc.DailyPrayerTimes.TimeFormat.Should().Be("h:mm A");
    }

    // ── Tolerant parsing ───────────────────────────────────────────────────────

    [Fact]
    public void Deserialize_DocumentWithUnknownFields_DoesNotThrow()
    {
        // Documents with extra unknown fields (forward compat) must parse without error.
        const string json = """
            {
              "salahapi": "2.0",
              "unknownTopLevelField": "ignored",
              "info": { "name": "Test", "futureField": 42 },
              "location": { "timezone": "UTC", "newField": true }
            }
            """;

        var act = () => Deserialize(json);

        act.Should().NotThrow();
        var doc = act();
        doc!.Version.Should().Be("2.0");
        doc!.Location!.Timezone.Should().Be("UTC");
    }

    [Fact]
    public void Deserialize_PartialDocument_MissingOptionalSections_DoesNotThrow()
    {
        // Only location present; all other sections absent.
        const string json = """
            {
              "salahapi": "1.1",
              "location": { "timezone": "America/New_York" }
            }
            """;

        var act = () => Deserialize(json);

        act.Should().NotThrow();
        var doc = act();
        doc!.Version.Should().Be("1.1");
        doc.Location!.Timezone.Should().Be("America/New_York");
        doc.CalculationMethod.Should().BeNull();
        doc.DailyPrayerTimes.Should().BeNull();
    }

    [Fact]
    public void Deserialize_EmptyObject_DoesNotThrow()
    {
        var act = () => Deserialize("{}");

        act.Should().NotThrow();
        var doc = act();
        doc.Should().NotBeNull();
        doc!.Version.Should().BeNull();
        doc.Location.Should().BeNull();
    }

    [Fact]
    public void Deserialize_IqamaRuleWithOnlyLatest_DoesNotThrow()
    {
        // Partial iqama rule - only one field present.
        const string json = """
            {
              "salahapi": "1.1",
              "calculationMethod": {
                "iqamaCalculationRules": {
                  "isha": { "latest": "23:00" }
                }
              }
            }
            """;

        var act = () => Deserialize(json);

        act.Should().NotThrow();
        var doc = act();
        var rule = doc!.CalculationMethod!.IqamaCalculationRules!.Isha!;
        rule.Latest.Should().Be("23:00");
        rule.AfterAthanMinutes.Should().BeNull();
    }

    // ── Helper ─────────────────────────────────────────────────────────────────

    private static SalahApiDocument? Deserialize(string json) =>
        JsonSerializer.Deserialize(json, SalahApiJsonContext.Default.SalahApiDocument);
}
