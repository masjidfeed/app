using FluentAssertions;
using MasjidApp.Core.Notifications;

namespace MasjidApp.Core.Tests.Notifications;

public sealed class PrayerNotificationPreferencesTests
{
    [Fact]
    public void Default_Preferences_HaveFajrAthanEnabled()
    {
        var prefs = new PrayerNotificationPreferences();
        prefs.FajrAthan.Should().BeTrue();
    }

    [Fact]
    public void Default_Preferences_HaveMinutesBeforeZero()
    {
        var prefs = new PrayerNotificationPreferences();
        prefs.MinutesBefore.Should().Be(0);
    }

    [Fact]
    public void RecordEquality_WithSameValues_AreEqual()
    {
        var prefs1 = new PrayerNotificationPreferences { FajrAthan = true, MinutesBefore = 5 };
        var prefs2 = new PrayerNotificationPreferences { FajrAthan = true, MinutesBefore = 5 };
        prefs1.Should().Be(prefs2);
    }

    [Fact]
    public void AllIqamas_ValidLeadTime_EnablesEveryIqamaOnly()
    {
        var prefs = PrayerNotificationPreferences.AllIqamas(10);

        prefs.Should().BeEquivalentTo(new PrayerNotificationPreferences
        {
            FajrAthan = false,
            FajrIqama = true,
            DhuhrIqama = true,
            AsrIqama = true,
            MaghribAthan = false,
            MaghribIqama = true,
            IshaIqama = true,
            MinutesBefore = 10,
        });
    }

    [Fact]
    public void AllIqamas_NegativeLeadTime_Throws()
    {
        var act = () => PrayerNotificationPreferences.AllIqamas(-1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
