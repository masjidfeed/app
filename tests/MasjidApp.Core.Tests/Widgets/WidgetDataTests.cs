using System.Text.Json;
using FluentAssertions;
using MasjidApp.Core.Widgets;

namespace MasjidApp.Core.Tests.Widgets;

// QA-02, TC-04: unit tests for the multi-day WidgetData model and its JSON
// serialization. Validates that the seven-day snapshot written to the shared widget
// container round-trips without data loss, defaults are sensible, and parsing is
// tolerant of older/extra payloads (NFR-RL-01, FR-WG-05).

public sealed class WidgetDataTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    private const long DayOneStart = 1_749_700_800L; // a fixed midnight epoch

    private static WidgetDayData MakeDay(int index)
    {
        var dayStart = DayOneStart + (index * 86_400L);
        return new WidgetDayData
        {
            DateEpochSeconds = dayStart,
            HijriDate = $"{15 + index} Dhul-Hijjah 1446",
            GregorianDate = $"Day {12 + index} Jun 2026",
            Prayers =
            [
                new WidgetPrayerEntry("Fajr",    "4:30 AM", "4:45 AM", dayStart + 27_000L, dayStart + 27_900L),
                new WidgetPrayerEntry("Sunrise", "6:00 AM", string.Empty, dayStart + 32_400L, 0L),
                new WidgetPrayerEntry("Dhuhr",   "1:10 PM", "1:20 PM", dayStart + 47_400L, dayStart + 48_000L),
                new WidgetPrayerEntry("Asr",     "5:05 PM", "5:20 PM", dayStart + 61_500L, dayStart + 62_400L),
                new WidgetPrayerEntry("Maghrib", "8:45 PM", "8:50 PM", dayStart + 74_700L, dayStart + 75_000L),
                new WidgetPrayerEntry("Isha",    "10:30 PM", "10:45 PM", dayStart + 81_000L, dayStart + 81_900L),
            ],
        };
    }

    private static WidgetData MakeSampleData(int dayCount = 7) => new()
    {
        MasjidName = "Islamic Center",
        Timezone = "America/Los_Angeles",
        GeneratedAtEpochSeconds = DayOneStart + 100L,
        Days = Enumerable.Range(0, dayCount).Select(MakeDay).ToList(),
    };

    // ── Default-value tests ────────────────────────────────────────────────────

    [Fact]
    public void DefaultWidgetData_HasEmptyStringsAndNoDays()
    {
        var data = new WidgetData();

        data.MasjidName.Should().BeEmpty();
        data.Timezone.Should().BeEmpty();
        data.GeneratedAtEpochSeconds.Should().Be(0L);
        data.Days.Should().BeEmpty();
    }

    [Fact]
    public void DefaultWidgetDayData_HasEmptyValues()
    {
        var day = new WidgetDayData();

        day.DateEpochSeconds.Should().Be(0L);
        day.HijriDate.Should().BeEmpty();
        day.GregorianDate.Should().BeEmpty();
        day.Prayers.Should().BeEmpty();
    }

    [Fact]
    public void DefaultWidgetPrayerEntry_HasExpectedValues()
    {
        var entry = new WidgetPrayerEntry("Fajr", "4:30 AM", "4:45 AM");

        entry.Name.Should().Be("Fajr");
        entry.AthanTime.Should().Be("4:30 AM");
        entry.IqamaTime.Should().Be("4:45 AM");
        // Epochs are optional and default to zero for forward compatibility.
        entry.AthanEpochSeconds.Should().Be(0L);
        entry.IqamaEpochSeconds.Should().Be(0L);
    }

    // ── Serialization tests ────────────────────────────────────────────────────

    [Fact]
    public void Serialize_WidgetData_ProducesCamelCaseJson()
    {
        var data = MakeSampleData();

        var json = JsonSerializer.Serialize(data, WidgetDataJsonContext.Default.WidgetData);

        json.Should().Contain("\"masjidName\"");
        json.Should().Contain("\"timezone\"");
        json.Should().Contain("\"generatedAtEpochSeconds\"");
        json.Should().Contain("\"days\"");
        json.Should().Contain("\"dateEpochSeconds\"");
        json.Should().Contain("\"hijriDate\"");
        json.Should().Contain("\"gregorianDate\"");
        json.Should().Contain("\"prayers\"");
        json.Should().Contain("\"athanTime\"");
        json.Should().Contain("\"iqamaTime\"");
        json.Should().Contain("\"athanEpochSeconds\"");
        json.Should().Contain("\"iqamaEpochSeconds\"");
    }

    [Fact]
    public void Serialize_WidgetData_ContainsExpectedValues()
    {
        var data = MakeSampleData();

        var json = JsonSerializer.Serialize(data, WidgetDataJsonContext.Default.WidgetData);

        json.Should().Contain("Islamic Center");
        json.Should().Contain("15 Dhul-Hijjah 1446");
        json.Should().Contain("Dhuhr");
    }

    [Fact]
    public void Deserialize_ValidJson_RestoresTopLevelFields()
    {
        var original = MakeSampleData();
        var json = JsonSerializer.Serialize(original, WidgetDataJsonContext.Default.WidgetData);

        var restored = JsonSerializer.Deserialize(json, WidgetDataJsonContext.Default.WidgetData);

        restored.Should().NotBeNull();
        restored!.MasjidName.Should().Be(original.MasjidName);
        restored.Timezone.Should().Be(original.Timezone);
        restored.GeneratedAtEpochSeconds.Should().Be(original.GeneratedAtEpochSeconds);
        restored.Days.Should().HaveCount(7);
    }

    [Fact]
    public void RoundTrip_Days_PreservesAllDaysAndPrayers()
    {
        var original = MakeSampleData();
        var json = JsonSerializer.Serialize(original, WidgetDataJsonContext.Default.WidgetData);

        var restored = JsonSerializer.Deserialize(json, WidgetDataJsonContext.Default.WidgetData);

        restored!.Days.Should().HaveCount(7);
        var firstDay = restored.Days[0];
        firstDay.DateEpochSeconds.Should().Be(DayOneStart);
        firstDay.HijriDate.Should().Be("15 Dhul-Hijjah 1446");
        firstDay.Prayers.Should().HaveCount(6);
        firstDay.Prayers[0].Name.Should().Be("Fajr");
        firstDay.Prayers[0].AthanTime.Should().Be("4:30 AM");
        firstDay.Prayers[0].IqamaTime.Should().Be("4:45 AM");
        firstDay.Prayers[0].AthanEpochSeconds.Should().Be(DayOneStart + 27_000L);
        firstDay.Prayers[0].IqamaEpochSeconds.Should().Be(DayOneStart + 27_900L);
        firstDay.Prayers[1].Name.Should().Be("Sunrise");
        firstDay.Prayers[1].IqamaTime.Should().BeEmpty();
        firstDay.Prayers[1].IqamaEpochSeconds.Should().Be(0L);
        firstDay.Prayers[5].Name.Should().Be("Isha");

        // The last day's start should be six days after the first.
        restored.Days[6].DateEpochSeconds.Should().Be(DayOneStart + (6 * 86_400L));
    }

    [Fact]
    public void Deserialize_DayWithoutPrayers_DefaultsToEmptyList()
    {
        // Forward/backward compatibility: a day missing the prayers key still decodes.
        const string json = """
            {
                "masjidName": "Test",
                "days": [ { "dateEpochSeconds": 1749700800, "hijriDate": "1 Muharram 1447" } ]
            }
            """;

        var restored = JsonSerializer.Deserialize(json, WidgetDataJsonContext.Default.WidgetData);

        restored!.Days.Should().ContainSingle();
        restored.Days[0].Prayers.Should().BeEmpty();
        restored.Days[0].DateEpochSeconds.Should().Be(1_749_700_800L);
    }

    [Fact]
    public void Deserialize_PrayerEntryWithoutEpochs_DefaultsToZero()
    {
        // Forward/backward compatibility: a prayer entry that omits the epoch keys must
        // still decode with the epochs defaulting to zero.
        const string json = """
            {
                "masjidName": "Test",
                "days": [ { "prayers": [ { "name": "Fajr", "athanTime": "5:00 AM", "iqamaTime": "5:15 AM" } ] } ]
            }
            """;

        var restored = JsonSerializer.Deserialize(json, WidgetDataJsonContext.Default.WidgetData);

        restored!.Days[0].Prayers.Should().ContainSingle();
        restored.Days[0].Prayers[0].AthanEpochSeconds.Should().Be(0L);
        restored.Days[0].Prayers[0].IqamaEpochSeconds.Should().Be(0L);
    }

    [Fact]
    public void RoundTrip_EmptyDays_PreservesEmptyList()
    {
        var data = new WidgetData { MasjidName = "Test" };
        var json = JsonSerializer.Serialize(data, WidgetDataJsonContext.Default.WidgetData);

        var restored = JsonSerializer.Deserialize(json, WidgetDataJsonContext.Default.WidgetData);

        restored!.Days.Should().BeEmpty();
    }

    [Fact]
    public void Deserialize_ExtraJsonFields_DoesNotThrow()
    {
        // NFR-RL-01: tolerant parsing - unknown fields must be ignored.
        const string json = """
            {
                "masjidName": "Test Masjid",
                "timezone": "UTC",
                "generatedAtEpochSeconds": 1000,
                "days": [],
                "unknownField": "should be ignored",
                "anotherUnknown": 42
            }
            """;

        var act = () => JsonSerializer.Deserialize(json, WidgetDataJsonContext.Default.WidgetData);

        act.Should().NotThrow();
        var result = act();
        result!.MasjidName.Should().Be("Test Masjid");
    }

    [Fact]
    public void Serialize_LargeEpochSeconds_PreservesFullPrecision()
    {
        const long epoch = 9_999_999_999L;
        var data = new WidgetData
        {
            GeneratedAtEpochSeconds = epoch,
            Days =
            [
                new WidgetDayData
                {
                    DateEpochSeconds = epoch,
                    Prayers = [new WidgetPrayerEntry("Fajr", "5:00 AM", "5:15 AM", epoch, epoch + 600)],
                },
            ],
        };

        var json = JsonSerializer.Serialize(data, WidgetDataJsonContext.Default.WidgetData);
        var restored = JsonSerializer.Deserialize(json, WidgetDataJsonContext.Default.WidgetData);

        restored!.GeneratedAtEpochSeconds.Should().Be(epoch);
        restored.Days[0].DateEpochSeconds.Should().Be(epoch);
        restored.Days[0].Prayers[0].AthanEpochSeconds.Should().Be(epoch);
        restored.Days[0].Prayers[0].IqamaEpochSeconds.Should().Be(epoch + 600);
    }
}
