using FluentAssertions;
using MasjidApp.Core.Events;
using MasjidApp.Core.Models;

namespace MasjidApp.Core.Tests.Events;

// QA-02: unit tests for EventsService pure query methods.

/// <summary>Unit tests for <see cref="EventsService"/> pure query methods.</summary>
public sealed class EventsServiceTests
{
    // Pass null! for the client - only pure (non-network) methods are tested here.
    private readonly EventsService _sut = new(null!);

    private static readonly DateTimeOffset BaseTime =
        new DateTimeOffset(2026, 6, 12, 21, 0, 0, TimeSpan.FromHours(-7));

    private static MasjidPost ClassEvent => new()
    {
        Title = "Weekly Halaqa",
        Description = "<p>Join us every Friday for a study circle.</p>",
        EventDateTime = BaseTime,
        Category = "classes",
        RecurrenceRule = "FREQ=WEEKLY;BYDAY=FR",
    };

    private static MasjidPost FundraiserEvent => new()
    {
        Title = "Community Fundraising Dinner",
        Description = "<p>Annual fundraiser to support operations.</p>",
        EventDateTime = new DateTimeOffset(2026, 6, 20, 18, 30, 0, TimeSpan.FromHours(-7)),
        Category = "fundraisers",
    };

    private static MasjidPost PastEvent => new()
    {
        Title = "Old Community Picnic",
        Description = "<p>A picnic from last year.</p>",
        EventDateTime = new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero),
        Category = "community",
    };

    // ── GetUpcoming ─────────────────────────────────────────────────────────────

    [Fact]
    public void GetUpcoming_ReturnsSortedByOccurrenceDateTime()
    {
        var from = BaseTime.AddDays(-1);
        var to = BaseTime.AddDays(14);

        // FundraiserEvent (Jun 20) comes after ClassEvent occurrences (Jun 12, Jun 19).
        var result = _sut.GetUpcoming(new[] { FundraiserEvent, ClassEvent }, from, to);

        result.Should().NotBeEmpty();
        result.Should().BeInAscendingOrder(o => o.OccurrenceDateTime);
    }

    [Fact]
    public void GetUpcoming_RecurringEvent_ExpandsMultipleOccurrences()
    {
        var from = BaseTime;
        var to = BaseTime.AddDays(21); // 3 weeks → should yield 4 Fridays (Jun 12,19,26, Jul 3)

        var result = _sut.GetUpcoming(new[] { ClassEvent }, from, to);

        result.Should().HaveCountGreaterThanOrEqualTo(3);
        result.Should().AllSatisfy(o => o.Event.Title.Should().Be("Weekly Halaqa"));
    }

    [Fact]
    public void GetUpcoming_ExcludesPastOneTimeEvents()
    {
        var from = BaseTime;
        var to = BaseTime.AddDays(30);

        var result = _sut.GetUpcoming(new[] { PastEvent, FundraiserEvent }, from, to);

        result.Should().NotContain(o => o.Event.Title == "Old Community Picnic");
        result.Should().Contain(o => o.Event.Title == "Community Fundraising Dinner");
    }

    [Fact]
    public void GetUpcoming_HandlesDefaultDateTimeOffset_DoesNotThrow()
    {
        // An event with a missing/default eventDateTime (NFR-RL-01: tolerate malformed).
        var defaultEvent = new MasjidPost { Title = "Malformed", EventDateTime = default };
        var from = BaseTime;
        var to = BaseTime.AddDays(30);

        var act = () => _sut.GetUpcoming(new[] { defaultEvent }, from, to);

        act.Should().NotThrow();
        act().Should().BeEmpty();
    }

    [Fact]
    public void GetUpcoming_AlertAtEvaluationTime_ExposesAlertOnOccurrence()
    {
        var alertEnd = BaseTime.AddHours(1);
        var ev = FundraiserEvent with
        {
            Alert = "Dinner moved to the main hall.",
            AlertEndDateTime = alertEnd,
        };

        var result = _sut.GetUpcoming([ev], BaseTime, BaseTime.AddDays(30));

        result.Should().ContainSingle();
        result[0].ActiveAlert.Should().Be("Dinner moved to the main hall.");
        result[0].HasActiveAlert.Should().BeTrue();
    }

    [Fact]
    public void GetUpcoming_AlertAtEndDateTime_RemainsActive()
    {
        var ev = FundraiserEvent with
        {
            Alert = "Dinner moved to the main hall.",
            AlertEndDateTime = BaseTime,
        };

        var result = _sut.GetUpcoming([ev], BaseTime, BaseTime.AddDays(30));

        result.Should().ContainSingle();
        result[0].HasActiveAlert.Should().BeTrue();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void GetUpcoming_IncompleteOrExpiredAlert_DoesNotExposeAlert(
        bool hasEndDate,
        bool isExpired)
    {
        var ev = FundraiserEvent with
        {
            Alert = "Dinner moved to the main hall.",
            AlertEndDateTime = hasEndDate
                ? BaseTime.AddMinutes(isExpired ? -1 : 1)
                : null,
        };

        var result = _sut.GetUpcoming([ev], BaseTime, BaseTime.AddDays(30));

        result.Should().ContainSingle();
        result[0].ActiveAlert.Should().BeNull();
        result[0].HasActiveAlert.Should().BeFalse();
    }

    // ── FilterByCategory ────────────────────────────────────────────────────────

    [Fact]
    public void FilterByCategory_IsCaseInsensitive()
    {
        var from = BaseTime;
        var to = BaseTime.AddDays(30);
        var occurrences = _sut.GetUpcoming(new[] { ClassEvent, FundraiserEvent }, from, to);

        // Query with uppercase - should still match "classes".
        var result = _sut.FilterByCategory(occurrences, "CLASSES");

        result.Should().NotBeEmpty();
        result.Should().AllSatisfy(o => o.Event.Category.Should().Be("classes"));
    }

    [Fact]
    public void FilterByCategory_ExcludesOtherCategories()
    {
        var from = BaseTime;
        var to = BaseTime.AddDays(30);
        var occurrences = _sut.GetUpcoming(new[] { ClassEvent, FundraiserEvent }, from, to);

        var result = _sut.FilterByCategory(occurrences, "fundraisers");

        result.Should().AllSatisfy(o => o.Event.Category.Should().Be("fundraisers"));
        result.Should().NotContain(o => o.Event.Category == "classes");
    }

    // ── Search ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Search_MatchesTitleCaseInsensitively()
    {
        var from = BaseTime;
        var to = BaseTime.AddDays(30);
        var occurrences = _sut.GetUpcoming(new[] { ClassEvent, FundraiserEvent }, from, to);

        var result = _sut.Search(occurrences, "halaqa");

        result.Should().NotBeEmpty();
        result.Should().AllSatisfy(o => o.Event.Title.Should().ContainEquivalentOf("halaqa"));
    }

    [Fact]
    public void Search_MatchesDescriptionCaseInsensitively()
    {
        var from = BaseTime;
        var to = BaseTime.AddDays(30);
        var occurrences = _sut.GetUpcoming(new[] { FundraiserEvent }, from, to);

        var result = _sut.Search(occurrences, "ANNUAL");

        result.Should().NotBeEmpty();
    }

    [Fact]
    public void Search_ReturnsEmptyWhenNoMatch()
    {
        var from = BaseTime;
        var to = BaseTime.AddDays(30);
        var occurrences = _sut.GetUpcoming(new[] { ClassEvent, FundraiserEvent }, from, to);

        var result = _sut.Search(occurrences, "zzz_no_match_zzz");

        result.Should().BeEmpty();
    }
}
