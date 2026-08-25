using FluentAssertions;
using MasjidApp.Core.Theming;

namespace MasjidApp.Core.Tests.Theming;

public class ThemePreferenceTests
{
    [Theory]
    [InlineData(ThemePreference.System, ThemePreference.Light)]
    [InlineData(ThemePreference.Light, ThemePreference.Dark)]
    [InlineData(ThemePreference.Dark, ThemePreference.System)]
    public void Next_FromPreference_ReturnsNextInCycle(ThemePreference current, ThemePreference expected)
    {
        current.Next().Should().Be(expected);
    }

    [Fact]
    public void Next_AppliedThreeTimes_ReturnsToStart()
    {
        ThemePreference.System.Next().Next().Next().Should().Be(ThemePreference.System);
    }
}
