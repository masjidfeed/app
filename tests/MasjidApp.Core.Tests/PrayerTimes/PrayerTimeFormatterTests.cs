using System.Globalization;
using FluentAssertions;
using MasjidApp.Core.PrayerTimes;

namespace MasjidApp.Core.Tests.PrayerTimes;

// QA-02, FR-ST-03: user-selected 12/24-hour time formatting must be deterministic.

public sealed class PrayerTimeFormatterTests
{
    [Fact]
    public void Format_Use24HourTimeFalseWithMeridiem_ReturnsTwelveHourTime()
    {
        var result = PrayerTimeFormatter.Format(new TimeOnly(17, 5), use24HourTime: false);

        result.Should().Be(new TimeOnly(17, 5).ToString("h:mm tt", CultureInfo.CurrentCulture));
    }

    [Fact]
    public void Format_Use24HourTimeFalseWithoutMeridiem_ReturnsCompactTwelveHourTime()
    {
        var result = PrayerTimeFormatter.Format(new TimeOnly(17, 5), use24HourTime: false, includeMeridiem: false);

        result.Should().Be(new TimeOnly(17, 5).ToString("h:mm", CultureInfo.CurrentCulture));
    }

    [Fact]
    public void Format_Use24HourTimeTrueWithMeridiem_ReturnsTwentyFourHourTimeWithoutMeridiem()
    {
        var result = PrayerTimeFormatter.Format(new TimeOnly(17, 5), use24HourTime: true);

        result.Should().Be(new TimeOnly(17, 5).ToString("HH:mm", CultureInfo.CurrentCulture));
    }

    [Fact]
    public void Format_NullTime_ReturnsEmptyText()
    {
        var result = PrayerTimeFormatter.Format(null, use24HourTime: true);

        result.Should().Be("-");
    }
}
