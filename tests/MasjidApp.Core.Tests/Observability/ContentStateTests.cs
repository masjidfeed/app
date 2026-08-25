using FluentAssertions;
using MasjidApp.Core.Http;
using MasjidApp.Core.Observability;

namespace MasjidApp.Core.Tests.Observability;

// NFR-OF-02: unit tests for ContentState freshness logic.

public sealed class ContentStateTests
{
    [Fact]
    public void FromApiResult_FreshResult_IsOfflineFalseIsStaleFlase()
    {
        var fetchedAt = DateTimeOffset.UtcNow;
        var result = new ApiResult<string>.Fresh("data", fetchedAt);

        var state = ContentState.FromApiResult(result);

        state.IsOffline.Should().BeFalse();
        state.IsStale.Should().BeFalse();
        state.LastUpdated.Should().Be(fetchedAt);
    }

    [Fact]
    public void FromApiResult_StaleResult_IsOfflineTrue()
    {
        var fetchedAt = DateTimeOffset.UtcNow;
        var result = new ApiResult<string>.Stale("cached", fetchedAt, null);

        var state = ContentState.FromApiResult(result);

        state.IsOffline.Should().BeTrue();
    }

    [Fact]
    public void FromApiResult_OldFetchedAtBeyondThreshold_IsStaleTrue()
    {
        var oldFetchedAt = DateTimeOffset.UtcNow - TimeSpan.FromHours(25);
        var result = new ApiResult<string>.Fresh("old data", oldFetchedAt);

        var state = ContentState.FromApiResult(result);

        state.IsStale.Should().BeTrue();
    }

    [Fact]
    public void FromApiResult_RecentFetchedAt_IsstaleFalse()
    {
        var recentFetchedAt = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(30);
        var result = new ApiResult<string>.Fresh("recent data", recentFetchedAt);

        var state = ContentState.FromApiResult(result);

        state.IsStale.Should().BeFalse();
    }

    [Fact]
    public void FromApiResult_FailedResultWithNoFetchedAt_IsOfflineTrueLastUpdatedNull()
    {
        var result = new ApiResult<string>.Failed(null, "network error");

        var state = ContentState.FromApiResult(result);

        state.IsOffline.Should().BeTrue();
        state.LastUpdated.Should().BeNull();
    }

    [Fact]
    public void Combine_OneFeedFailed_AggregateIsOfflineWithOldestTimestamp()
    {
        var oldest = DateTimeOffset.UtcNow.AddHours(-2);
        var newest = oldest.AddHours(1);

        var state = ContentState.Combine(
            new ContentState(false, newest, false),
            new ContentState(true, null, false),
            new ContentState(false, oldest, true));

        state.IsOffline.Should().BeTrue();
        state.IsStale.Should().BeTrue();
        state.LastUpdated.Should().Be(oldest);
    }
}
