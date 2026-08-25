using FluentAssertions;
using MasjidApp.Core.Widgets;

namespace MasjidApp.Core.Tests.Widgets;

// QA-02, FR-WG-01, FR-WG-06: unit tests for the shared widget day-selection logic that
// both the Android (C#) and iOS (Swift mirror) widgets use, so the two platforms stay
// in parity when picking the current day, current prayer, next prayer, and staleness state.

public sealed class WidgetDaySelectorTests
{
    private const long DayOneStart = 1_749_700_800L; // a fixed midnight epoch
    private const long SecondsPerDay = 86_400L;

    private static WidgetDayData MakeDay(int index)
    {
        var dayStart = DayOneStart + (index * SecondsPerDay);
        return new WidgetDayData
        {
            DateEpochSeconds = dayStart,
            HijriDate = $"Day {index} Hijri",
            GregorianDate = $"Day {index} Gregorian",
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

    private static WidgetData MakeData(int dayCount = 7) => new()
    {
        MasjidName = "Test",
        Timezone = "America/Los_Angeles",
        GeneratedAtEpochSeconds = DayOneStart,
        Days = Enumerable.Range(0, dayCount).Select(MakeDay).ToList(),
    };

    // ── CoverageEndEpochSeconds ─────────────────────────────────────────────────

    [Fact]
    public void CoverageEndEpochSeconds_IsLastDayStartPlusOneDay()
    {
        var data = MakeData();

        var end = WidgetDaySelector.CoverageEndEpochSeconds(data);

        end.Should().Be(DayOneStart + (6 * SecondsPerDay) + SecondsPerDay);
    }

    [Fact]
    public void CoverageEndEpochSeconds_NoDays_IsZero()
    {
        WidgetDaySelector.CoverageEndEpochSeconds(new WidgetData()).Should().Be(0L);
    }

    // ── IsStale ─────────────────────────────────────────────────────────────────

    [Fact]
    public void IsStale_WithinWindow_IsFalse()
    {
        var data = MakeData();

        // Midday on the first covered day.
        WidgetDaySelector.IsStale(data, DayOneStart + 50_000L).Should().BeFalse();
    }

    [Fact]
    public void IsStale_OnLastCoveredDay_IsFalse()
    {
        var data = MakeData();

        // Midday on the seventh (last) covered day is still within coverage.
        var lastDayMidday = DayOneStart + (6 * SecondsPerDay) + 50_000L;
        WidgetDaySelector.IsStale(data, lastDayMidday).Should().BeFalse();
    }

    [Fact]
    public void IsStale_PastCoverageEnd_IsTrue()
    {
        var data = MakeData();

        var pastEnd = WidgetDaySelector.CoverageEndEpochSeconds(data) + 1;
        WidgetDaySelector.IsStale(data, pastEnd).Should().BeTrue();
    }

    [Fact]
    public void IsStale_NoDays_IsFalse()
    {
        WidgetDaySelector.IsStale(new WidgetData(), DayOneStart).Should().BeFalse();
    }

    // ── CurrentDay ──────────────────────────────────────────────────────────────

    [Fact]
    public void CurrentDay_PicksDayContainingTheInstant()
    {
        var data = MakeData();

        // Midday on the third day (index 2).
        var midThirdDay = DayOneStart + (2 * SecondsPerDay) + 50_000L;
        var day = WidgetDaySelector.CurrentDay(data, midThirdDay);

        day.Should().NotBeNull();
        day!.DateEpochSeconds.Should().Be(DayOneStart + (2 * SecondsPerDay));
    }

    [Fact]
    public void CurrentDay_BeforeWindow_ReturnsFirstDay()
    {
        var data = MakeData();

        var day = WidgetDaySelector.CurrentDay(data, DayOneStart - 1000L);

        day!.DateEpochSeconds.Should().Be(DayOneStart);
    }

    [Fact]
    public void CurrentDay_PastWindow_ReturnsLastDay()
    {
        var data = MakeData();

        var pastEnd = WidgetDaySelector.CoverageEndEpochSeconds(data) + 100_000L;
        var day = WidgetDaySelector.CurrentDay(data, pastEnd);

        day!.DateEpochSeconds.Should().Be(DayOneStart + (6 * SecondsPerDay));
    }

    [Fact]
    public void CurrentDay_NoDays_ReturnsNull()
    {
        WidgetDaySelector.CurrentDay(new WidgetData(), DayOneStart).Should().BeNull();
    }

    // ── NextPrayerName ──────────────────────────────────────────────────────────

    [Fact]
    public void NextPrayerName_BeforeFajr_ReturnsFajr()
    {
        var data = MakeData();

        // Just after midnight on the first day, before Fajr iqama.
        var name = WidgetDaySelector.NextPrayerName(data, DayOneStart + 1000L);

        name.Should().Be("Fajr");
    }

    [Fact]
    public void NextPrayerName_BetweenDhuhrAndAsr_ReturnsAsr()
    {
        var data = MakeData();

        // After Dhuhr iqama (dayStart + 48000) but before Asr iqama (dayStart + 62400).
        var name = WidgetDaySelector.NextPrayerName(data, DayOneStart + 50_000L);

        name.Should().Be("Asr");
    }

    [Fact]
    public void NextPrayerName_AfterLastPrayerOfDay_RollsToNextDayFajr()
    {
        var data = MakeData();

        // After Isha iqama on the first day → next is Fajr the following day.
        var afterIsha = DayOneStart + 82_000L;
        var name = WidgetDaySelector.NextPrayerName(data, afterIsha);

        name.Should().Be("Fajr");
    }

    [Fact]
    public void NextPrayerName_PastWindow_ReturnsNull()
    {
        var data = MakeData();

        var pastEnd = WidgetDaySelector.CoverageEndEpochSeconds(data) + 100_000L;
        WidgetDaySelector.NextPrayerName(data, pastEnd).Should().BeNull();
    }

    [Fact]
    public void NextPrayerName_NoDays_ReturnsNull()
    {
        WidgetDaySelector.NextPrayerName(new WidgetData(), DayOneStart).Should().BeNull();
    }

    [Fact]
    public void NextCountdownTarget_BeforeAthan_ReturnsAthan()
    {
        var data = MakeData();

        var target = WidgetDaySelector.NextCountdownTarget(data, DayOneStart + 26_000L);

        target.Should().NotBeNull();
        target!.Prayer.Name.Should().Be("Fajr");
        target.Kind.Should().Be(WidgetTransitionKind.Athan);
        target.EpochSeconds.Should().Be(DayOneStart + 27_000L);
    }

    [Fact]
    public void NextCountdownTarget_BetweenAthanAndIqama_ReturnsIqama()
    {
        var data = MakeData();

        var target = WidgetDaySelector.NextCountdownTarget(data, DayOneStart + 27_001L);

        target.Should().NotBeNull();
        target!.Prayer.Name.Should().Be("Fajr");
        target.Kind.Should().Be(WidgetTransitionKind.Iqama);
        target.EpochSeconds.Should().Be(DayOneStart + 27_900L);
    }

    [Fact]
    public void NextCountdownTarget_AfterIqama_ReturnsNextAthan()
    {
        var data = MakeData();

        var target = WidgetDaySelector.NextCountdownTarget(data, DayOneStart + 27_901L);

        target.Should().NotBeNull();
        target!.Prayer.Name.Should().Be("Sunrise");
        target.Kind.Should().Be(WidgetTransitionKind.Athan);
        target.EpochSeconds.Should().Be(DayOneStart + 32_400L);
    }

    [Fact]
    public void RemainingProgress_HalfwayFromAthanToIqama_ReturnsHalf()
    {
        var data = MakeData();
        var target = WidgetDaySelector.NextCountdownTarget(data, DayOneStart + 27_001L)!;

        var progress = WidgetDaySelector.RemainingProgress(
            data,
            target,
            DayOneStart + 27_450L);

        progress.Should().BeApproximately(0.5d, 0.0001d);
    }

    // ── CurrentPrayerName ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(26_999L, "Isha")]
    [InlineData(27_000L, "Fajr")]
    [InlineData(31_499L, "Fajr")]
    [InlineData(31_500L, "Sunrise")]
    [InlineData(33_299L, "Sunrise")]
    [InlineData(33_300L, "Dhuhr")]
    [InlineData(61_499L, "Dhuhr")]
    [InlineData(61_500L, "Asr")]
    [InlineData(73_799L, "Asr")]
    [InlineData(73_800L, "Maghrib")]
    [InlineData(75_599L, "Maghrib")]
    [InlineData(75_600L, "Isha")]
    public void CurrentPrayerName_AtBackgroundBoundary_ReturnsExpectedPeriod(
        long secondsAfterDayStart,
        string expected)
    {
        var data = MakeData();

        var name = WidgetDaySelector.CurrentPrayerName(data, DayOneStart + secondsAfterDayStart);

        name.Should().Be(expected);
    }

    [Fact]
    public void CurrentPrayerName_AfterLastPrayerOfDay_ReturnsIsha()
    {
        var data = MakeData();

        var name = WidgetDaySelector.CurrentPrayerName(data, DayOneStart + 82_000L);

        name.Should().Be("Isha");
    }

    [Fact]
    public void CurrentPrayerName_BeforeFirstCoveredFajr_ReturnsIshaNightFallback()
    {
        var data = MakeData();

        var name = WidgetDaySelector.CurrentPrayerName(data, DayOneStart + 1000L);

        name.Should().Be("Isha");
    }

    [Fact]
    public void CurrentPrayerName_NoDays_ReturnsNull()
    {
        WidgetDaySelector.CurrentPrayerName(new WidgetData(), DayOneStart).Should().BeNull();
    }

    [Theory]
    [InlineData(31_500L, "Fajr")]
    [InlineData(73_800L, "Asr")]
    public void CurrentPrayerPeriodName_DuringUpcomingSkyWindow_ReturnsAthanPeriod(
        long secondsAfterDayStart,
        string expected)
    {
        var data = MakeData();

        var name = WidgetDaySelector.CurrentPrayerPeriodName(data, DayOneStart + secondsAfterDayStart);

        name.Should().Be(expected);
    }
}
