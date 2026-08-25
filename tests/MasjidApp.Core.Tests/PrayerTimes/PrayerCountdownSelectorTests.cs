using FluentAssertions;
using MasjidApp.Core.PrayerTimes;

namespace MasjidApp.Core.Tests.PrayerTimes;

// QA-02, FR-PT-02, FR-WG-01: page countdowns must match the widget transition rule.

public sealed class PrayerCountdownSelectorTests
{
    private static readonly DateOnly TestDate = new(2026, 6, 1);

    [Fact]
    public void NextEvent_BeforeAthan_ReturnsAthan()
    {
        var schedule = MakeSchedule(TestDate);
        var now = ToUtc(TestDate, new TimeOnly(13, 0));

        var target = PrayerCountdownSelector.NextEvent([schedule], now);

        target.Should().NotBeNull();
        target!.Name.Should().Be("Dhuhr");
        target.Kind.Should().Be(PrayerEventKind.Athan);
        target.TimeRemaining.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void NextEvent_AfterAthan_ReturnsIqama()
    {
        var schedule = MakeSchedule(TestDate);
        var now = ToUtc(TestDate, new TimeOnly(13, 15));

        var target = PrayerCountdownSelector.NextEvent([schedule], now);

        target.Should().NotBeNull();
        target!.Name.Should().Be("Dhuhr");
        target.Kind.Should().Be(PrayerEventKind.Iqama);
        target.TimeRemaining.Should().Be(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void NextTransition_AfterAthanBeforeIqama_ReturnsSamePrayerIqama()
    {
        var schedule = MakeSchedule(TestDate);
        var now = ToUtc(TestDate, new TimeOnly(13, 15));

        var target = PrayerCountdownSelector.NextTransition([schedule], now);

        target.Should().NotBeNull();
        target!.Name.Should().Be("Dhuhr");
        target.OccursAtUtc.Should().Be(ToUtc(TestDate, new TimeOnly(13, 30)));
        target.TimeRemaining.Should().Be(TimeSpan.FromMinutes(15));
    }

    [Fact]
    public void NextTransition_AfterFajrIqamaBeforeSunrise_ReturnsSunriseAthanFallback()
    {
        var schedule = MakeSchedule(TestDate);
        var now = ToUtc(TestDate, new TimeOnly(4, 55));

        var target = PrayerCountdownSelector.NextTransition([schedule], now);

        target.Should().NotBeNull();
        target!.Name.Should().Be("Sunrise");
        target.OccursAtUtc.Should().Be(ToUtc(TestDate, new TimeOnly(6, 0)));
    }

    [Fact]
    public void NextTransition_MissingIqama_ReturnsPrayerAthanFallback()
    {
        var schedule = MakeSchedule(TestDate) with { AsrIqama = null };
        var now = ToUtc(TestDate, new TimeOnly(13, 45));

        var target = PrayerCountdownSelector.NextTransition([schedule], now);

        target.Should().NotBeNull();
        target!.Name.Should().Be("Asr");
        target.OccursAtUtc.Should().Be(ToUtc(TestDate, new TimeOnly(17, 5)));
    }

    [Fact]
    public void NextTransition_AfterLastPrayer_UsesNextCoveredDayFajrIqama()
    {
        var nextDate = TestDate.AddDays(1);
        var schedules = new[]
        {
            MakeSchedule(TestDate),
            MakeSchedule(nextDate) with
            {
                FajrAthan = new TimeOnly(4, 25),
                FajrIqama = new TimeOnly(4, 40),
            },
        };
        var now = ToUtc(TestDate, new TimeOnly(23, 0));

        var target = PrayerCountdownSelector.NextTransition(schedules, now);

        target.Should().NotBeNull();
        target!.Name.Should().Be("Fajr");
        target.OccursAtUtc.Should().Be(ToUtc(nextDate, new TimeOnly(4, 40)));
    }

    [Fact]
    public void NextTransition_NoFutureTransition_ReturnsNull()
    {
        var schedule = MakeSchedule(TestDate);
        var now = ToUtc(TestDate, new TimeOnly(23, 0));

        PrayerCountdownSelector.NextTransition([schedule], now).Should().BeNull();
    }

    [Theory]
    [InlineData(4, 29, 59, "Isha")]
    [InlineData(4, 30, 0, "Fajr")]
    [InlineData(5, 44, 59, "Fajr")]
    [InlineData(5, 45, 0, "Sunrise")]
    [InlineData(6, 14, 59, "Sunrise")]
    [InlineData(6, 15, 0, "Dhuhr")]
    [InlineData(17, 4, 59, "Dhuhr")]
    [InlineData(17, 5, 0, "Asr")]
    [InlineData(20, 29, 59, "Asr")]
    [InlineData(20, 30, 0, "Maghrib")]
    [InlineData(20, 59, 59, "Maghrib")]
    [InlineData(21, 0, 0, "Isha")]
    public void CurrentPrayerName_AtBackgroundBoundary_ReturnsExpectedPeriod(
        int hour,
        int minute,
        int second,
        string expected)
    {
        var schedule = MakeSchedule(TestDate);
        var now = ToUtc(TestDate, new TimeOnly(hour, minute, second));

        PrayerCountdownSelector.CurrentPrayerName([schedule], now).Should().Be(expected);
    }

    [Fact]
    public void CurrentPrayerName_BeforeFajrWithOnlyToday_ReturnsIshaNightFallback()
    {
        var schedule = MakeSchedule(TestDate);
        var now = ToUtc(TestDate, new TimeOnly(2, 30));

        PrayerCountdownSelector.CurrentPrayerName([schedule], now).Should().Be("Isha");
    }

    [Fact]
    public void CurrentPrayerName_AcrossCoveredDays_ReturnsPreviousDayIsha()
    {
        var previousDate = TestDate.AddDays(-1);
        var schedules = new[]
        {
            MakeSchedule(previousDate),
            MakeSchedule(TestDate),
        };
        var now = ToUtc(TestDate, new TimeOnly(2, 30));

        PrayerCountdownSelector.CurrentPrayerName(schedules, now).Should().Be("Isha");
    }

    [Theory]
    [InlineData(5, 45, "Fajr")]
    [InlineData(20, 30, "Asr")]
    public void CurrentPrayerPeriodName_DuringUpcomingSkyWindow_ReturnsAthanPeriod(
        int hour,
        int minute,
        string expected)
    {
        var schedule = MakeSchedule(TestDate);
        var now = ToUtc(TestDate, new TimeOnly(hour, minute));

        PrayerCountdownSelector.CurrentPrayerPeriodName([schedule], now).Should().Be(expected);
    }

    private static DailyPrayerSchedule MakeSchedule(DateOnly date) => new()
    {
        Date = date,
        Timezone = "America/Los_Angeles",
        FajrAthan = new TimeOnly(4, 30),
        FajrIqama = new TimeOnly(4, 45),
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

    private static DateTimeOffset ToUtc(DateOnly date, TimeOnly time)
    {
        var timezone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
        var utc = TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(time), timezone);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }
}
