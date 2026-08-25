using FluentAssertions;
using MasjidApp.Core.Utils;

namespace MasjidApp.Core.Tests.Utils;

public sealed class ExternalLinkPolicyTests
{
    [Theory]
    [InlineData("https://example.com/path")]
    [InlineData("http://localhost:8080/path")]
    public void GetSafeUri_HttpOrHttps_ReturnsUri(string value)
    {
        ExternalLinkPolicy.GetSafeUri(value).Should().NotBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/relative")]
    [InlineData("javascript:alert(1)")]
    [InlineData("mailto:test@example.com")]
    public void GetSafeUri_UnsafeOrRelative_ReturnsNull(string? value)
    {
        ExternalLinkPolicy.GetSafeUri(value).Should().BeNull();
    }
}
