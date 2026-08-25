using FluentAssertions;
using MasjidApp.Core.PrayerTimes;

namespace MasjidApp.Core.Tests.PrayerTimes;

public sealed class PrayerEventCountdownFormatterTests
{
    [Fact]
    public void Format_MoreThanOneHourUntilAthan_IncludesHoursAndMinutes()
    {
        var target = Target(PrayerEventKind.Athan, TimeSpan.FromMinutes(130));

        PrayerEventCountdownFormatter.Format(target).Should().Be("2 hr 10 min till Asr Athan");
    }

    [Fact]
    public void Format_LessThanOneHourUntilAthan_OmitsHours()
    {
        var target = Target(PrayerEventKind.Athan, TimeSpan.FromMinutes(10));

        PrayerEventCountdownFormatter.Format(target).Should().Be("10 min till Asr Athan");
    }

    [Fact]
    public void Format_UpcomingIqama_LabelsIqama()
    {
        var target = Target(PrayerEventKind.Iqama, TimeSpan.FromMinutes(15));

        PrayerEventCountdownFormatter.Format(target).Should().Be("15 min till Asr Iqama");
    }

    private static UpcomingPrayerEvent Target(PrayerEventKind kind, TimeSpan remaining) =>
        new("Asr", kind, DateTimeOffset.UnixEpoch + remaining, remaining);
}