using System.Globalization;
using FluentAssertions;
using MasjidApp.Core.Notifications;
using MasjidApp.Core.PrayerTimes;

namespace MasjidApp.Core.Tests.Notifications;

// QA-02, FR-PT-10, FR-ST-03, TC-05: reminder wording is composed from localized templates,
// so the composition rules must be deterministic and culture-aware.

public sealed class PrayerReminderMessageTests
{
    private static readonly TimeOnly Iqama = new(5, 30);

    private static readonly PrayerReminderMessageTemplates Templates = new()
    {
        TitleFormat = "Get ready for {0}",
        LeadFormat = "{0} {1} in {2} ({3})",
        AtTimeFormat = "{0} {1} at {2}",
        IqamaLabel = "iqama",
        AthanLabel = "athan",
        MinuteFormat = "{0} minute",
        MinutesFormat = "{0} minutes",
        HourFormat = "{0} hour",
        HoursFormat = "{0} hours",
    };

    [Fact]
    public void BuildBody_LeadTime_DescribesPrayerLeadTimeAndTime()
    {
        var body = Build(minutesBefore: 10);

        body.Should().Be($"Fajr iqama in 10 minutes ({Formatted(use24HourTime: false)})");
    }

    [Fact]
    public void BuildBody_SingleMinuteLeadTime_UsesSingularMinute()
    {
        Build(minutesBefore: 1).Should().StartWith("Fajr iqama in 1 minute (");
    }

    [Fact]
    public void BuildBody_ZeroLeadTime_AnnouncesTheTimeItself()
    {
        var body = Build(minutesBefore: 0);

        body.Should().Be($"Fajr iqama at {Formatted(use24HourTime: false)}");
    }

    [Fact]
    public void BuildBody_NegativeLeadTime_FallsBackToAtTimeWording()
    {
        Build(minutesBefore: -5).Should().Be($"Fajr iqama at {Formatted(use24HourTime: false)}");
    }

    [Fact]
    public void BuildBody_TwentyFourHourPreference_FormatsTimeOnTwentyFourHourClock()
    {
        var body = Build(minutesBefore: 10, use24HourTime: true);

        body.Should().Be($"Fajr iqama in 10 minutes ({Formatted(use24HourTime: true)})");
    }

    [Fact]
    public void BuildBody_AthanLabel_UsesSuppliedEventWord()
    {
        Build(minutesBefore: 10, eventLabel: Templates.AthanLabel)
            .Should().StartWith("Fajr athan in 10 minutes (");
    }

    [Fact]
    public void BuildTitle_PrayerName_FormatsGetReadyTitle()
    {
        PrayerReminderMessage.BuildTitle("Fajr", Templates).Should().Be("Get ready for Fajr");
    }

    [Fact]
    public void BuildTitle_NullTemplates_Throws()
    {
        var act = () => PrayerReminderMessage.BuildTitle("Fajr", null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildTitle_EmptyPrayerName_Throws()
    {
        var act = () => PrayerReminderMessage.BuildTitle(" ", Templates);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(5, "5 minutes")]
    [InlineData(45, "45 minutes")]
    [InlineData(60, "1 hour")]
    [InlineData(90, "1 hour 30 minutes")]
    [InlineData(120, "2 hours")]
    [InlineData(121, "2 hours 1 minute")]
    public void FormatLeadTime_KnownLeadTimes_ReadNaturally(int minutesBefore, string expected)
    {
        PrayerReminderMessage.FormatLeadTime(minutesBefore, Templates).Should().Be(expected);
    }

    [Fact]
    public void FormatLeadTime_NegativeMinutes_Throws()
    {
        var act = () => PrayerReminderMessage.FormatLeadTime(-1, Templates);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void BuildBody_NullTemplates_Throws()
    {
        var act = () => PrayerReminderMessage.BuildBody("Fajr", "iqama", 10, Iqama, false, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildBody_EmptyPrayerName_Throws()
    {
        var act = () => PrayerReminderMessage.BuildBody(" ", "iqama", 10, Iqama, false, Templates);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BuildBody_EmptyEventLabel_Throws()
    {
        var act = () => PrayerReminderMessage.BuildBody("Fajr", string.Empty, 10, Iqama, false, Templates);

        act.Should().Throw<ArgumentException>();
    }

    private static string Build(
        int minutesBefore,
        bool use24HourTime = false,
        string? eventLabel = null) =>
        PrayerReminderMessage.BuildBody(
            "Fajr",
            eventLabel ?? Templates.IqamaLabel,
            minutesBefore,
            Iqama,
            use24HourTime,
            Templates);

    private static string Formatted(bool use24HourTime) =>
        Iqama.ToString(use24HourTime ? "HH:mm" : "h:mm tt", CultureInfo.CurrentCulture);
}
