using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using MasjidApp.Core.PrayerTimes;
using MasjidApp.Core.SalahApi;

namespace MasjidApp.Core.Tests.PrayerTimes;

// QA-02, QA-06: unit tests for the prayer-time and iqama calculation engine (Batch A2).
// Validates against Bothell WA (ISNA) reference values, iqama rule logic, override priority,
// Jumuah generation, and performance (NFR-PF-02).

public sealed class PrayerTimeCalculatorTests
{
    // ── Test document fixture (matches mock/salahapi.json) ────────────────────

    /// <summary>
    /// Standard ISNA document for Bothell, WA - the canonical reference fixture.
    /// lat=47.7623, lon=-122.2054, tz=America/Los_Angeles.
    /// </summary>
    private static SalahApiDocument BothellIsnaDocument() => new()
    {
        Location = new SalahApiLocation
        {
            Latitude = 47.7623,
            Longitude = -122.2054,
            Elevation = 30,
            Timezone = "America/Los_Angeles",
        },
        CalculationMethod = new SalahApiCalculationMethod
        {
            Name = "ISNA",
            AsrCalculationMethod = "Standard",
            HighLatitudeAdjustment = "AngleBased",
            IqamaCalculationRules = new SalahApiIqamaCalculationRules
            {
                Fajr = new SalahApiPrayerCalculationRule
                {
                    AfterAthanMinutes = 20,
                    RoundMinutes = 5,
                    Overrides =
                    [
                        new SalahApiPrayerCalculationOverride
                        {
                            Condition = "ramadan",
                            Time = new SalahApiPrayerCalculationRule { AfterAthanMinutes = 25 },
                        },
                    ],
                },
                Dhuhr = new SalahApiPrayerCalculationRule { Static = "13:30" },
                Asr = new SalahApiPrayerCalculationRule { AfterAthanMinutes = 15, RoundMinutes = 5 },
                Maghrib = new SalahApiPrayerCalculationRule { AfterAthanMinutes = 5 },
                Isha = new SalahApiPrayerCalculationRule
                {
                    AfterAthanMinutes = 15,
                    RoundMinutes = 5,
                    Earliest = "20:00",
                    Latest = "22:30",
                    Overrides =
                    [
                        new SalahApiPrayerCalculationOverride
                        {
                            Condition = "daylightSavingsTime",
                            Time = new SalahApiPrayerCalculationRule { AfterAthanMinutes = 15, Latest = "23:00" },
                        },
                        new SalahApiPrayerCalculationOverride
                        {
                            Condition = "ramadan",
                            Time = new SalahApiPrayerCalculationRule { Static = "22:00" },
                        },
                    ],
                },
            },
            JumuahRules =
            [
                new SalahApiJumuahRule
                {
                    Name = "First Jumuah",
                    Time = new SalahApiPrayerCalculationRule { Static = "13:00" },
                    Location = new SalahApiJumuahLocation { Name = "Main Prayer Hall" },
                },
                new SalahApiJumuahRule
                {
                    Name = "Second Jumuah",
                    Time = new SalahApiPrayerCalculationRule { Static = "14:15" },
                    Location = new SalahApiJumuahLocation { Name = "Community Hall" },
                },
            ],
        },
    };

    private static readonly PrayerTimeCalculator _sut = new();

    // ── 1. Basic athan calculation ────────────────────────────────────────────

    [Fact]
    public void Calculate_BothellIsna_ReturnsFajrAthan_NonDefault()
    {
        // Arrange - a non-Ramadan, non-DST winter date
        var date = new DateOnly(2026, 1, 15);

        // Act
        var schedule = _sut.Calculate(BothellIsnaDocument(), date);

        // Assert - Fajr must be a real time, not the fallback MinValue
        schedule.FajrAthan.Should().NotBe(TimeOnly.MinValue);
        schedule.FajrAthan.Hour.Should().BeGreaterThan(4);   // sanity: Fajr before 8am
        schedule.FajrAthan.Hour.Should().BeLessThan(8);
    }

    [Fact]
    public void Calculate_BothellIsna_AllSixAthanTimesPopulated()
    {
        // Arrange
        var date = new DateOnly(2026, 3, 15); // Ramadan 2026 - picked deliberately; Fajr/Isha iqama will use Ramadan override

        // Act
        var schedule = _sut.Calculate(BothellIsnaDocument(), date);

        // Assert - all athan times must be non-MinValue
        schedule.FajrAthan.Should().NotBe(TimeOnly.MinValue);
        schedule.Sunrise.Should().NotBe(TimeOnly.MinValue);
        schedule.DhuhrAthan.Should().NotBe(TimeOnly.MinValue);
        schedule.AsrAthan.Should().NotBe(TimeOnly.MinValue);
        schedule.MaghribAthan.Should().NotBe(TimeOnly.MinValue);
        schedule.IshaAthan.Should().NotBe(TimeOnly.MinValue);
    }

    // ── 2. Static iqama ───────────────────────────────────────────────────────

    [Fact]
    public void Calculate_StaticDhuhrIqama_ReturnsExactTime()
    {
        // Arrange - Dhuhr iqama is configured as "static": "13:30"
        var date = new DateOnly(2026, 1, 15); // non-Ramadan, non-DST

        // Act
        var schedule = _sut.Calculate(BothellIsnaDocument(), date);

        // Assert
        schedule.DhuhrIqama.Should().Be(new TimeOnly(13, 30));
    }

    // ── 3. AfterAthanMinutes iqama ────────────────────────────────────────────

    [Fact]
    public void Calculate_MaghribIqamaAfterAtan_IsAthanPlusFiveMinutes()
    {
        // Arrange - Maghrib iqama: afterAthanMinutes=5, no rounding, no Ramadan/DST override
        var date = new DateOnly(2026, 1, 15); // non-Ramadan, non-DST winter date

        // Act
        var schedule = _sut.Calculate(BothellIsnaDocument(), date);

        // Assert - iqama should be exactly athan + 5 minutes
        schedule.MaghribIqama.Should().NotBeNull();
        var expected = schedule.MaghribAthan.AddMinutes(5);
        schedule.MaghribIqama!.Value.Should().Be(expected);
    }

    [Fact]
    public void Calculate_FajrBeforeEndMinutes_UsesSunriseAsWindowEnd()
    {
        var document = BothellIsnaDocument() with
        {
            CalculationMethod = BothellIsnaDocument().CalculationMethod! with
            {
                IqamaCalculationRules = BothellIsnaDocument().CalculationMethod!.IqamaCalculationRules! with
                {
                    Fajr = new SalahApiPrayerCalculationRule { BeforeEndMinutes = 30 },
                },
            },
        };

        var schedule = _sut.Calculate(document, new DateOnly(2026, 1, 15));

        schedule.FajrIqama.Should().Be(schedule.Sunrise.AddMinutes(-30));
    }

    // ── 4. RoundMinutes ──────────────────────────────────────────────────────

    [Fact]
    public void Calculate_FajrIqamaRounded_IsMultipleOfRoundMinutes()
    {
        // Arrange - Fajr iqama: afterAthanMinutes=20, roundMinutes=5
        // (non-Ramadan date so base rule applies, not Ramadan override)
        var date = new DateOnly(2026, 1, 15); // January: not Ramadan 2026 (starts ~Feb 18)

        // Act
        var schedule = _sut.Calculate(BothellIsnaDocument(), date);

        // Assert - result must be a multiple of 5 minutes
        schedule.FajrIqama.Should().NotBeNull();
        (schedule.FajrIqama!.Value.Minute % 5).Should().Be(0);
    }

    [Fact]
    public void Calculate_RoundUpToNextMultiple_VerifyManually()
    {
        // Arrange - document with afterAthanMinutes=17, roundMinutes=5
        // Fajr athan on 2026-01-15 (Bothell) is around 06:27.
        // 06:27 + 17 min = 06:44 → rounded UP to next 5 min multiple = 06:45.
        var doc = BothellIsnaDocument() with
        {
            CalculationMethod = BothellIsnaDocument().CalculationMethod! with
            {
                IqamaCalculationRules = BothellIsnaDocument().CalculationMethod!.IqamaCalculationRules! with
                {
                    Fajr = new SalahApiPrayerCalculationRule { AfterAthanMinutes = 17, RoundMinutes = 5 },
                },
            },
        };
        var date = new DateOnly(2026, 1, 15);

        // Act
        var schedule = _sut.Calculate(doc, date);

        // Assert - result is a multiple of 5
        schedule.FajrIqama.Should().NotBeNull();
        (schedule.FajrIqama!.Value.Minute % 5).Should().Be(0);
        // And it must be >= athan + 17 min
        schedule.FajrIqama!.Value.Should().BeOnOrAfter(schedule.FajrAthan.AddMinutes(17));
    }

    // ── 5. Ramadan override ───────────────────────────────────────────────────

    [Fact]
    public void Calculate_RamadanDate_IshaIqamaUsesStaticRamadanOverride()
    {
        // Arrange - 2026-03-15 is during Ramadan 2026 (Ramadan starts ~2026-02-18).
        // Ramadan override for Isha: static "22:00"
        var date = new DateOnly(2026, 3, 15);

        // Act
        var schedule = _sut.Calculate(BothellIsnaDocument(), date);

        // Assert - Isha iqama must be the Ramadan static value 22:00
        schedule.IshaIqama.Should().Be(new TimeOnly(22, 0));
    }

    [Fact]
    public void Calculate_RamadanDate_FajrIqamaUsesRamadanAfterAthanMinutes()
    {
        // Arrange - Ramadan override for Fajr: afterAthanMinutes=25 (no rounding)
        var date = new DateOnly(2026, 3, 15); // Ramadan

        // Act
        var schedule = _sut.Calculate(BothellIsnaDocument(), date);

        // Assert - Fajr iqama = athan + 25 min (no rounding in Ramadan override)
        schedule.FajrIqama.Should().NotBeNull();
        schedule.FajrIqama!.Value.Should().Be(schedule.FajrAthan.AddMinutes(25));
    }

    // ── 6. Friday: Jumuah times ───────────────────────────────────────────────

    [Fact]
    public void Calculate_Friday_JumuahTimesPopulated()
    {
        // Arrange - 2026-01-16 is a Friday
        var date = new DateOnly(2026, 1, 16);
        date.DayOfWeek.Should().Be(DayOfWeek.Friday);

        // Act
        var schedule = _sut.Calculate(BothellIsnaDocument(), date);

        // Assert
        schedule.JumuahTimes.Should().HaveCount(2);
        schedule.JumuahTimes[0].Time.Should().Be(new TimeOnly(13, 0));
        schedule.JumuahTimes[0].Name.Should().Be("First Jumuah");
        schedule.JumuahTimes[1].Time.Should().Be(new TimeOnly(14, 15));
    }

    // ── 7. Non-Friday: Jumuah still reported ──────────────────────────────────

    [Fact]
    public void Calculate_NonFriday_JumuahTimesStillPopulated()
    {
        // Arrange - 2026-01-15 is a Thursday. The published Jumuah schedule is surfaced
        // every day so worshippers can plan ahead (FR-PT-03).
        var date = new DateOnly(2026, 1, 15);
        date.DayOfWeek.Should().Be(DayOfWeek.Thursday);

        // Act
        var schedule = _sut.Calculate(BothellIsnaDocument(), date);

        // Assert
        schedule.JumuahTimes.Should().HaveCount(2);
        schedule.JumuahTimes[0].Time.Should().Be(new TimeOnly(13, 0));
    }

    [Fact]
    public void Calculate_ProductionJumuahTimeObjects_PopulatesSessions()
    {
        const string json = """
                        {
                            "location": {
                                "latitude": 47.7716065,
                                "longitude": -122.1942708,
                                "timezone": "America/Los_Angeles"
                            },
                            "calculationMethod": {
                                "name": "isna",
                                "asrCalculationMethod": "hanafi",
                                "highLatitudeAdjustment": "middleOfTheNight",
                                "jumuahRules": [
                                    { "name": "Jumuah 1", "time": { "static": "13:30" } },
                                    { "name": "Jumuah 2", "time": { "static": "14:30" } }
                                ]
                            }
                        }
                        """;
        var document = JsonSerializer.Deserialize(
                json,
                SalahApiJsonContext.Default.SalahApiDocument)!;

        var schedule = _sut.Calculate(document, new DateOnly(2026, 8, 2));

        schedule.JumuahTimes.Should().Equal(
                new JumuahTime("Jumuah 1", new TimeOnly(13, 30), null),
                new JumuahTime("Jumuah 2", new TimeOnly(14, 30), null));
    }

    // ── 7b. No jumuahRules: Jumuah empty ──────────────────────────────────────

    [Fact]
    public void Calculate_NoJumuahRules_JumuahTimesEmpty()
    {
        var document = BothellIsnaDocument() with
        {
            CalculationMethod = BothellIsnaDocument().CalculationMethod! with { JumuahRules = null },
        };

        var schedule = _sut.Calculate(document, new DateOnly(2026, 1, 16));

        schedule.JumuahTimes.Should().BeEmpty("the section is hidden when none are published");
    }

    [Fact]
    public void Calculate_NonStaticJumuahRules_OmitsSessions()
    {
        var document = BothellIsnaDocument() with
        {
            CalculationMethod = BothellIsnaDocument().CalculationMethod! with
            {
                JumuahRules =
                [
                    new SalahApiJumuahRule
                    {
                        Name = "Dynamic Jumuah",
                        Time = new SalahApiPrayerCalculationRule { AfterAthanMinutes = 30 },
                    },
                    new SalahApiJumuahRule
                    {
                        Name = "Disabled Jumuah",
                        Time = new SalahApiPrayerCalculationRule { Static = "none" },
                    },
                ],
            },
        };

        var schedule = _sut.Calculate(document, new DateOnly(2026, 1, 16));

        schedule.JumuahTimes.Should().BeEmpty();
    }

    // ── 8. Performance: 30-day window < 3000 ms ──────────────────────────────

    [Fact]
    public void Calculate_ThirtyDays_CompletesWithin3000Ms()
    {
        // Arrange
        var doc = BothellIsnaDocument();
        var start = new DateOnly(2026, 3, 1); // spans DST transition (US: 2026-03-08) and Ramadan

        var sw = Stopwatch.StartNew();

        // Act
        for (var i = 0; i < 30; i++)
        {
            _ = _sut.Calculate(doc, start.AddDays(i));
        }

        sw.Stop();

        // Assert - must complete well within the 3 s budget (NFR-PF-02 per-day: 100 ms)
        sw.ElapsedMilliseconds.Should().BeLessThan(3000);
    }

    // ── 9. Timezone field propagated ─────────────────────────────────────────

    [Fact]
    public void Calculate_TimezoneField_MatchesDocument()
    {
        var date = new DateOnly(2026, 1, 15);
        var schedule = _sut.Calculate(BothellIsnaDocument(), date);
        schedule.Timezone.Should().Be("America/Los_Angeles");
    }

    // ── 10. Missing location gracefully defaults ──────────────────────────────

    [Fact]
    public void Calculate_NullLocation_DoesNotThrow()
    {
        // Arrange - minimal document with no location (NFR-RL-01)
        var doc = new SalahApiDocument
        {
            CalculationMethod = new SalahApiCalculationMethod { Name = "ISNA" },
        };
        var date = new DateOnly(2026, 1, 15);

        // Act
        var act = () => _sut.Calculate(doc, date);

        // Assert - must not throw; times will be MinValue but no crash
        act.Should().NotThrow();
    }
}
