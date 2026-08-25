using FluentAssertions;
using MasjidApp.Core.Http;

namespace MasjidApp.Core.Tests.Http;

// FR-CM-01, FR-EV-01, API-09, NFR-RL-01

public class FeedRefreshPolicyTests
{
    private sealed record Payload(string Value);

    [Fact]
    public void ShouldReplaceItems_FreshResult_ReturnsTrue()
    {
        var result = new ApiResult<Payload>.Fresh(new Payload("a"), DateTimeOffset.UtcNow);

        FeedRefreshPolicy.ShouldReplaceItems(result).Should().BeTrue();
    }

    [Fact]
    public void ShouldReplaceItems_StaleResult_ReturnsTrue()
    {
        var result = new ApiResult<Payload>.Stale(new Payload("a"), DateTimeOffset.UtcNow, null);

        FeedRefreshPolicy.ShouldReplaceItems(result).Should().BeTrue();
    }

    [Fact]
    public void ShouldReplaceItems_FailedResult_ReturnsFalse()
    {
        var result = new ApiResult<Payload>.Failed(null, "offline");

        FeedRefreshPolicy.ShouldReplaceItems(result).Should().BeFalse();
    }

    [Fact]
    public void ShouldReplaceItems_NullResult_ReturnsFalse()
    {
        FeedRefreshPolicy.ShouldReplaceItems<Payload>(null).Should().BeFalse();
    }
}
