using FluentAssertions;
using MasjidApp.Core.PrayerTimes;

namespace MasjidApp.Core.Tests.PrayerTimes;

// QA-02, NFR-RL-01: unit tests for CSV prayer-times parser (Batch A3).

public sealed class CsvPrayerTimesParserTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string MakeRow(string date, string fajr = "05:30", string sunrise = "07:00",
        string dhuhr = "12:15", string asr = "15:45", string maghrib = "19:30", string isha = "21:00")
        => $"{date},{fajr},{sunrise},{dhuhr},{asr},{maghrib},{isha}";

    // ── Tests ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_ValidCsvWithHeaderRow_SkipsHeaderAndReturnsRows()
    {
        var csv = string.Join("\n",
            "date,fajr,sunrise,dhuhr,asr,maghrib,isha",
            MakeRow("2026-01-01", "06:15", "07:52", "12:11", "14:28", "16:30", "18:09"),
            MakeRow("2026-01-02", "06:16", "07:53", "12:12", "14:29", "16:31", "18:10"));

        var result = CsvPrayerTimesParser.Parse(csv);

        result.Should().HaveCount(2);
        result[0].Date.Should().Be(new DateOnly(2026, 1, 1));
        result[0].Fajr.Should().Be(new TimeOnly(6, 15));
        result[0].Sunrise.Should().Be(new TimeOnly(7, 52));
        result[0].Dhuhr.Should().Be(new TimeOnly(12, 11));
        result[0].Asr.Should().Be(new TimeOnly(14, 28));
        result[0].Maghrib.Should().Be(new TimeOnly(16, 30));
        result[0].Isha.Should().Be(new TimeOnly(18, 9));

        result[1].Date.Should().Be(new DateOnly(2026, 1, 2));
        result[1].Fajr.Should().Be(new TimeOnly(6, 16));
    }

    [Fact]
    public void Parse_ValidCsvWithoutHeaderRow_ParsesAllRows()
    {
        var csv = string.Join("\n",
            MakeRow("2026-03-15", "05:44", "07:08", "13:17", "16:50", "19:25", "20:50"),
            MakeRow("2026-03-16", "05:42", "07:06", "13:17", "16:51", "19:26", "20:52"));

        var result = CsvPrayerTimesParser.Parse(csv);

        result.Should().HaveCount(2);
        result[0].Date.Should().Be(new DateOnly(2026, 3, 15));
        result[1].Date.Should().Be(new DateOnly(2026, 3, 16));
    }

    [Fact]
    public void Parse_MalformedRows_SkipsBadAndReturnGoodRows()
    {
        var csv = string.Join("\n",
            MakeRow("2026-01-01"), // good
            "not-a-date,06:00,07:30,12:00,15:00,18:00,20:00", // bad date
            "2026-01-02,BADTIME,07:00,12:00,15:00,18:00,20:00", // bad time
            "2026-01-03,06:00,07:00,12:00", // too few columns
            MakeRow("2026-01-04")); // good

        var result = CsvPrayerTimesParser.Parse(csv);

        result.Should().HaveCount(2);
        result[0].Date.Should().Be(new DateOnly(2026, 1, 1));
        result[1].Date.Should().Be(new DateOnly(2026, 1, 4));
    }

    [Fact]
    public void Parse_EmptyString_ReturnsEmptyList()
    {
        var result = CsvPrayerTimesParser.Parse(string.Empty);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Parse_AllBadRows_ReturnsEmptyListWithoutThrowing()
    {
        var csv = string.Join("\n",
            "garbage line",
            "another,bad",
            ",,,,,",
            "date,fajr,sunrise,dhuhr,asr,maghrib,isha");

        var act = () => CsvPrayerTimesParser.Parse(csv);

        act.Should().NotThrow();
        act().Should().BeEmpty();
    }

    [Fact]
    public void Parse_WhitespaceOnlyString_ReturnsEmptyList()
    {
        var result = CsvPrayerTimesParser.Parse("   \n   \t   ");

        result.Should().BeEmpty();
    }

    [Fact]
    public void Parse_RowsWithTrailingCarriageReturn_ParsesCorrectly()
    {
        var csv = MakeRow("2026-06-01") + "\r\n" + MakeRow("2026-06-02") + "\r\n";

        var result = CsvPrayerTimesParser.Parse(csv);

        result.Should().HaveCount(2);
    }

    // ── Real mock feed format (quoted, HH:mm:ss, athan + iqama columns) ──────────

    [Fact]
    public void Parse_QuotedHeaderWithAthanAndIqamaColumns_MapsAllTimesByName()
    {
        var csv = string.Join("\n",
            "\"day\",\"fajr_athan\",\"fajr_iqama\",\"sunrise\",\"dhuhr_athan\",\"dhuhr_iqama\"," +
            "\"asr_athan\",\"asr_iqama\",\"maghrib_athan\",\"maghrib_iqama\",\"isha_athan\",\"isha_iqama\"",
            "\"2026-06-01\",\"03:08:00\",\"04:45:00\",\"05:14:00\",\"13:07:00\",\"13:30:00\"," +
            "\"17:19:00\",\"18:30:00\",\"21:00:00\",\"21:10:00\",\"23:06:00\",\"23:16:00\"");

        var result = CsvPrayerTimesParser.Parse(csv);

        result.Should().ContainSingle();
        var row = result[0];
        row.Date.Should().Be(new DateOnly(2026, 6, 1));

        // Athan times
        row.Fajr.Should().Be(new TimeOnly(3, 8));
        row.Sunrise.Should().Be(new TimeOnly(5, 14));
        row.Dhuhr.Should().Be(new TimeOnly(13, 7));
        row.Asr.Should().Be(new TimeOnly(17, 19));
        row.Maghrib.Should().Be(new TimeOnly(21, 0));
        row.Isha.Should().Be(new TimeOnly(23, 6));

        // Iqama times
        row.FajrIqama.Should().Be(new TimeOnly(4, 45));
        row.DhuhrIqama.Should().Be(new TimeOnly(13, 30));
        row.AsrIqama.Should().Be(new TimeOnly(18, 30));
        row.MaghribIqama.Should().Be(new TimeOnly(21, 10));
        row.IshaIqama.Should().Be(new TimeOnly(23, 16));
    }

    [Fact]
    public void Parse_HeaderlessTwelveColumnRow_MapsAthanAndIqamaPositionally()
    {
        var csv = "2026-06-01,03:08:00,04:45:00,05:14:00,13:07:00,13:30:00," +
                  "17:19:00,18:30:00,21:00:00,21:10:00,23:06:00,23:16:00";

        var result = CsvPrayerTimesParser.Parse(csv);

        result.Should().ContainSingle();
        var row = result[0];
        row.Fajr.Should().Be(new TimeOnly(3, 8));
        row.FajrIqama.Should().Be(new TimeOnly(4, 45));
        row.Sunrise.Should().Be(new TimeOnly(5, 14));
        row.IshaIqama.Should().Be(new TimeOnly(23, 16));
    }

    [Fact]
    public void Parse_AthanOnlyRow_LeavesIqamaNull()
    {
        var csv = MakeRow("2026-06-01");

        var result = CsvPrayerTimesParser.Parse(csv);

        result.Should().ContainSingle();
        result[0].FajrIqama.Should().BeNull();
        result[0].DhuhrIqama.Should().BeNull();
        result[0].IshaIqama.Should().BeNull();
    }

    [Fact]
    public void Parse_RealMockPrayerTimesCsv_ParsesEveryDataRow()
    {
        var content = File.ReadAllText(LocateMockCsv());

        var result = CsvPrayerTimesParser.Parse(content);

        // The mock fixture covers 2026-06-01 .. 2026-06-30 (29 data rows present).
        result.Should().HaveCountGreaterThan(20);
        result.Should().OnlyContain(r => r.Date.Year == 2026 && r.Date.Month == 6);

        var first = result[0];
        first.Date.Should().Be(new DateOnly(2026, 6, 1));
        first.Fajr.Should().Be(new TimeOnly(3, 8));
        first.FajrIqama.Should().Be(new TimeOnly(4, 45));
        first.DhuhrIqama.Should().Be(new TimeOnly(13, 30));
    }

    private static string LocateMockCsv()
    {
        var probe = new DirectoryInfo(AppContext.BaseDirectory);
        while (probe is not null)
        {
            var candidate = Path.Combine(probe.FullName, "mock", "prayer-times.csv");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            probe = probe.Parent;
        }

        throw new FileNotFoundException("Could not locate mock/prayer-times.csv from test output directory.");
    }
}
