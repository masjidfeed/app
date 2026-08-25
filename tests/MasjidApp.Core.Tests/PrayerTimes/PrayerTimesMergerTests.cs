using FluentAssertions;
using MasjidApp.Core.PrayerTimes;

namespace MasjidApp.Core.Tests.PrayerTimes;

// QA-02, API-04: unit tests for the CSV/calculated prayer-times merge policy (Batch A3).

public sealed class PrayerTimesMergerTests
{
    // ── Fixtures ───────────────────────────────────────────────────────────────

    private static readonly DateOnly TestDate = new(2026, 6, 1);

    private static DailyPrayerSchedule MakeCalculated() => new()
    {
        Date = TestDate,
        Timezone = "America/Los_Angeles",
        FajrAthan = new TimeOnly(4, 30),
        FajrIqama = new TimeOnly(4, 50),
        Sunrise = new TimeOnly(6, 0),
        DhuhrAthan = new TimeOnly(13, 10),
        DhuhrIqama = new TimeOnly(13, 30),
        AsrAthan = new TimeOnly(17, 5),
        AsrIqama = new TimeOnly(17, 20),
        MaghribAthan = new TimeOnly(20, 45),
        MaghribIqama = new TimeOnly(20, 50),
        IshaAthan = new TimeOnly(22, 30),
        IshaIqama = new TimeOnly(22, 45),
    };

    private static CsvPrayerTimesRow MakeCsvRow() => new(
        Date: TestDate,
        Fajr: new TimeOnly(4, 22),
        Sunrise: new TimeOnly(5, 55),
        Dhuhr: new TimeOnly(13, 8),
        Asr: new TimeOnly(17, 1),
        Maghrib: new TimeOnly(20, 40),
        Isha: new TimeOnly(22, 22));

    // ── Tests ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Merge_WithCsvRow_AthanTimesFromCsvIqamaFromCalc()
    {
        var calculated = MakeCalculated();
        var csvRow = MakeCsvRow();

        var result = PrayerTimesMerger.Merge(calculated, csvRow);

        // Athan times must come from CSV
        result.FajrAthan.Should().Be(csvRow.Fajr);
        result.Sunrise.Should().Be(csvRow.Sunrise);
        result.DhuhrAthan.Should().Be(csvRow.Dhuhr);
        result.AsrAthan.Should().Be(csvRow.Asr);
        result.MaghribAthan.Should().Be(csvRow.Maghrib);
        result.IshaAthan.Should().Be(csvRow.Isha);

        // Iqama times must be preserved from calculated
        result.FajrIqama.Should().Be(calculated.FajrIqama);
        result.DhuhrIqama.Should().Be(calculated.DhuhrIqama);
        result.AsrIqama.Should().Be(calculated.AsrIqama);
        result.MaghribIqama.Should().Be(calculated.MaghribIqama);
        result.IshaIqama.Should().Be(calculated.IshaIqama);
    }

    [Fact]
    public void Merge_WithNullCsvRow_ReturnsCalculatedUnchanged()
    {
        var calculated = MakeCalculated();

        var result = PrayerTimesMerger.Merge(calculated, csvRow: null);

        result.Should().BeSameAs(calculated);
    }

    [Fact]
    public void Merge_WithCsvRow_DatePreservedFromCalculated()
    {
        var calculated = MakeCalculated();
        var csvRow = MakeCsvRow();

        var result = PrayerTimesMerger.Merge(calculated, csvRow);

        result.Date.Should().Be(calculated.Date);
    }

    [Fact]
    public void Merge_WithCsvRow_TimezonePreservedFromCalculated()
    {
        var calculated = MakeCalculated();
        var csvRow = MakeCsvRow();

        var result = PrayerTimesMerger.Merge(calculated, csvRow);

        result.Timezone.Should().Be(calculated.Timezone);
    }

    [Fact]
    public void Merge_WithCsvRow_JumuahTimesPreservedFromCalculated()
    {
        var jumuah = new JumuahTime("First Jumuah", new TimeOnly(13, 0), null);
        var calculated = MakeCalculated() with { JumuahTimes = [jumuah] };
        var csvRow = MakeCsvRow();

        var result = PrayerTimesMerger.Merge(calculated, csvRow);

        result.JumuahTimes.Should().ContainSingle()
            .Which.Should().Be(jumuah);
    }

    [Fact]
    public void Merge_WithCsvRow_NullIqamaRemainsNull()
    {
        var calculated = MakeCalculated() with
        {
            DhuhrIqama = null,
            IshaIqama = null,
        };
        var csvRow = MakeCsvRow();

        var result = PrayerTimesMerger.Merge(calculated, csvRow);

        result.DhuhrIqama.Should().BeNull();
        result.IshaIqama.Should().BeNull();
    }

    [Fact]
    public void Merge_WithCsvIqama_IqamaTimesFromCsvOverrideCalculated()
    {
        var calculated = MakeCalculated();
        var csvRow = MakeCsvRow() with
        {
            FajrIqama = new TimeOnly(4, 45),
            DhuhrIqama = new TimeOnly(13, 30),
            AsrIqama = new TimeOnly(18, 30),
            MaghribIqama = new TimeOnly(21, 10),
            IshaIqama = new TimeOnly(23, 16),
        };

        var result = PrayerTimesMerger.Merge(calculated, csvRow);

        result.FajrIqama.Should().Be(csvRow.FajrIqama);
        result.DhuhrIqama.Should().Be(csvRow.DhuhrIqama);
        result.AsrIqama.Should().Be(csvRow.AsrIqama);
        result.MaghribIqama.Should().Be(csvRow.MaghribIqama);
        result.IshaIqama.Should().Be(csvRow.IshaIqama);
    }

    [Fact]
    public void Merge_WithPartialCsvIqama_FallsBackToCalculatedForMissingPrayers()
    {
        var calculated = MakeCalculated();
        var csvRow = MakeCsvRow() with { DhuhrIqama = new TimeOnly(13, 30) };

        var result = PrayerTimesMerger.Merge(calculated, csvRow);

        result.DhuhrIqama.Should().Be(new TimeOnly(13, 30));
        result.FajrIqama.Should().Be(calculated.FajrIqama);
        result.IshaIqama.Should().Be(calculated.IshaIqama);
    }
}
