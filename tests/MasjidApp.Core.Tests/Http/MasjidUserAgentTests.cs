using FluentAssertions;
using MasjidApp.Core.Http;

namespace MasjidApp.Core.Tests.Http;

public sealed class MasjidUserAgentTests
{
    [Fact]
    public void Create_IosRelease_ReturnsApiFormat()
    {
        var result = MasjidUserAgent.Create("iOS", "2.4.1");

        result.ToString().Should().Be("MasjidApp-iOS/2.4.1");
    }

    [Fact]
    public void Create_VersionWithoutAlphanumericPrefix_ThrowsArgumentException()
    {
        var act = () => MasjidUserAgent.Create("iOS", ".2.4.1");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_ApplicationNameOverMaximumLength_ThrowsArgumentOutOfRangeException()
    {
        var act = () => MasjidUserAgent.Create(new string('a', 91), "2.4.1");

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}