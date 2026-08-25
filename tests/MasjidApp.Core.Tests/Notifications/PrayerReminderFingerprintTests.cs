using FluentAssertions;
using MasjidApp.Core.Notifications;
using MasjidApp.Core.PrayerTimes;

namespace MasjidApp.Core.Tests.Notifications;

public sealed class PrayerReminderFingerprintTests
{
    private static readonly DateOnly WindowStart = new(2026, 3, 14);
    private const string MasjidId = "masjid-1";

    [Fact]
    public void Compute_SameInputs_ProducesIdenticalFingerprint()
    {
        var first = Compute();
        var second = Compute();

        second.Should().Be(first);
    }

    [Fact]
    public void Compute_DifferentInstancesWithEqualValues_ProduceIdenticalFingerprint()
    {
        var first = Compute(schedules: [Day(WindowStart), Day(WindowStart.AddDays(1))]);
        var second = Compute(schedules: [Day(WindowStart), Day(WindowStart.AddDays(1))]);

        second.Should().Be(first);
    }

    [Fact]
    public void Compute_DifferentEnabledFlag_ProducesDifferentFingerprint()
    {
        Compute(enabled: false).Should().NotBe(Compute(enabled: true));
    }

    [Fact]
    public void Compute_DifferentLeadTime_ProducesDifferentFingerprint()
    {
        Compute(minutesBefore: 20).Should().NotBe(Compute(minutesBefore: 10));
    }

    [Fact]
    public void Compute_DifferentMasjid_ProducesDifferentFingerprint()
    {
        Compute(masjidId: "masjid-2").Should().NotBe(Compute(masjidId: MasjidId));
    }

    [Fact]
    public void Compute_NullMasjid_ProducesDifferentFingerprintThanNamedMasjid()
    {
        Compute(masjidId: null).Should().NotBe(Compute(masjidId: MasjidId));
    }

    [Fact]
    public void Compute_DifferentWindowStartDate_ProducesDifferentFingerprint()
    {
        Compute(windowStart: WindowStart.AddDays(1)).Should().NotBe(Compute());
    }

    [Fact]
    public void Compute_DifferentTimezone_ProducesDifferentFingerprint()
    {
        var shifted = Day(WindowStart) with { Timezone = "America/New_York" };

        Compute(schedules: [shifted]).Should().NotBe(Compute());
    }

    [Fact]
    public void Compute_MissingIqama_ProducesDifferentFingerprint()
    {
        var withoutFajrIqama = Day(WindowStart) with { FajrIqama = null };

        Compute(schedules: [withoutFajrIqama]).Should().NotBe(Compute());
    }

    [Fact]
    public void Compute_AdditionalDay_ProducesDifferentFingerprint()
    {
        Compute(schedules: [Day(WindowStart), Day(WindowStart.AddDays(1))])
            .Should().NotBe(Compute());
    }

    [Fact]
    public void Compute_EmptySchedules_ProducesDifferentFingerprintThanPopulatedWindow()
    {
        Compute(schedules: []).Should().NotBe(Compute());
    }

    [Theory]
    [InlineData("FajrIqama")]
    [InlineData("DhuhrIqama")]
    [InlineData("AsrIqama")]
    [InlineData("MaghribIqama")]
    [InlineData("IshaIqama")]
    public void Compute_ChangedIqamaTime_ProducesDifferentFingerprint(string prayer)
    {
        var baseline = Day(WindowStart);
        var changed = prayer switch
        {
            "FajrIqama" => baseline with { FajrIqama = baseline.FajrIqama!.Value.AddMinutes(1) },
            "DhuhrIqama" => baseline with { DhuhrIqama = baseline.DhuhrIqama!.Value.AddMinutes(1) },
            "AsrIqama" => baseline with { AsrIqama = baseline.AsrIqama!.Value.AddMinutes(1) },
            "MaghribIqama" => baseline with { MaghribIqama = baseline.MaghribIqama!.Value.AddMinutes(1) },
            _ => baseline with { IshaIqama = baseline.IshaIqama!.Value.AddMinutes(1) },
        };

        Compute(schedules: [changed]).Should().NotBe(Compute(schedules: [baseline]));
    }

    [Fact]
    public void Compute_ChangedAthanTimeOnly_ProducesSameFingerprint()
    {
        // Only iqama reminders are scheduled, so athan drift alone must not force a reschedule.
        var athanShifted = Day(WindowStart) with
        {
            FajrAthan = Day(WindowStart).FajrAthan.AddMinutes(3),
        };

        Compute(schedules: [athanShifted]).Should().Be(Compute());
    }

    [Fact]
    public void Compute_DifferentTimeFormatPreference_ProducesDifferentFingerprint()
    {
        // The notification body embeds a formatted time, so flipping the clock preference
        // must refresh the pending window.
        Compute(use24HourTime: true).Should().NotBe(Compute(use24HourTime: false));
    }

    [Fact]
    public void Compute_NullSchedules_Throws()
    {
        var act = () => PrayerReminderFingerprint.Compute(true, 10, MasjidId, WindowStart, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private static string Compute(
        bool enabled = true,
        int minutesBefore = 10,
        string? masjidId = MasjidId,
        DateOnly? windowStart = null,
        IReadOnlyList<DailyPrayerSchedule>? schedules = null,
        bool use24HourTime = false) =>
        PrayerReminderFingerprint.Compute(
            enabled,
            minutesBefore,
            masjidId,
            windowStart ?? WindowStart,
            schedules ?? [Day(windowStart ?? WindowStart)],
            use24HourTime);

    private static DailyPrayerSchedule Day(DateOnly date) => new()
    {
        Date = date,
        FajrAthan = new TimeOnly(5, 12),
        FajrIqama = new TimeOnly(5, 30),
        Sunrise = new TimeOnly(6, 40),
        DhuhrAthan = new TimeOnly(13, 10),
        DhuhrIqama = new TimeOnly(13, 30),
        AsrAthan = new TimeOnly(16, 45),
        AsrIqama = new TimeOnly(17, 0),
        MaghribAthan = new TimeOnly(19, 25),
        MaghribIqama = new TimeOnly(19, 35),
        IshaAthan = new TimeOnly(20, 50),
        IshaIqama = new TimeOnly(21, 0),
        Timezone = "America/Los_Angeles",
    };
}
