using FluentAssertions;
using MasjidApp.Core.Theming;

namespace MasjidApp.Core.Tests.Theming;

public sealed class BrandColorTests
{
    [Theory]
    [InlineData("#006a4e", "#006A4E")]
    [InlineData("006A4E", "#006A4E")]
    [InlineData("#abc", "#AABBCC")]
    public void Normalize_ValidRgb_ReturnsNormalizedHex(string value, string expected)
    {
        BrandColor.Normalize(value).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#12")]
    [InlineData("#GGGGGG")]
    [InlineData("#FF006A4E")]
    public void Normalize_InvalidValue_ReturnsNull(string? value)
    {
        BrandColor.Normalize(value).Should().BeNull();
    }
}
