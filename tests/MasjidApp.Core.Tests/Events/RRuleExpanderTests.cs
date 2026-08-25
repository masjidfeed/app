using FluentAssertions;
using MasjidApp.Core.Events;

namespace MasjidApp.Core.Tests.Events;

// QA-02, QA-06: unit tests for RRULE expansion correctness and resilience.

/// <summary>Unit tests for <see cref="RRuleExpander"/>.</summary>
public sealed class RRuleExpanderTests
{
    // dtStart = Friday 2026-06-12 21:00 PDT (-07:00)
    private static readonly DateTimeOffset FridayStart =
        new DateTimeOffset(2026, 6, 12, 21, 0, 0, TimeSpan.FromHours(-7));

    [Fact]
    public void Expand_WeeklyFriday_ReturnsCorrectOccurrences()
    {
        // 4-week window starting at dtStart - should yield Friday occurrences.
        var windowStart = FridayStart;
        var windowEnd = FridayStart.AddDays(28);

        var result = RRuleExpander.Expand("FREQ=WEEKLY;BYDAY=FR", FridayStart, windowStart, windowEnd);

        result.Should().HaveCountGreaterThanOrEqualTo(4);
        result.Should().AllSatisfy(d => d.DayOfWeek.Should().Be(DayOfWeek.Friday));
        result.Should().BeInAscendingOrder();
        result[0].Should().Be(FridayStart);
    }

    [Fact]
    public void Expand_WeeklyWithCount_StopsAtCount()
    {
        // Sunday dtStart, COUNT=8 - must produce exactly 8 occurrences.
        var sundayStart = new DateTimeOffset(2026, 6, 14, 19, 0, 0, TimeSpan.FromHours(-7));
        var windowStart = sundayStart;
        var windowEnd = sundayStart.AddDays(70); // window large enough to fit all 8

        var result = RRuleExpander.Expand("FREQ=WEEKLY;BYDAY=SU;COUNT=8", sundayStart, windowStart, windowEnd);

        result.Should().HaveCount(8);
        result.Should().AllSatisfy(d => d.DayOfWeek.Should().Be(DayOfWeek.Sunday));
        result.Should().BeInAscendingOrder();
    }

    [Fact]
    public void Expand_NullRRule_ReturnsEmpty()
    {
        var result = RRuleExpander.Expand(null, FridayStart, FridayStart, FridayStart.AddDays(30));

        result.Should().BeEmpty();
    }

    [Fact]
    public void Expand_MalformedRRule_ReturnsEmpty()
    {
        // NFR-RL-01: must not throw on bad input, and must return empty.
        var result = RRuleExpander.Expand("NOT_A_VALID_RRULE_STRING", FridayStart, FridayStart, FridayStart.AddDays(30));

        result.Should().BeEmpty();
    }

    [Fact]
    public void Expand_Monthly_ReturnsCorrectOccurrences()
    {
        // Monthly recurrence starting on the 1st of each month.
        var monthlyStart = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var windowStart = monthlyStart;
        var windowEnd = new DateTimeOffset(2026, 6, 30, 23, 59, 59, TimeSpan.Zero);

        var result = RRuleExpander.Expand("FREQ=MONTHLY", monthlyStart, windowStart, windowEnd);

        result.Should().HaveCountGreaterThanOrEqualTo(6);
        result.Should().AllSatisfy(d => d.Day.Should().Be(1));
        result.Should().BeInAscendingOrder();
    }
}
