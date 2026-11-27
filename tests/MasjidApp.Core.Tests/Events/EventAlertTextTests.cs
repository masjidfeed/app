using FluentAssertions;
using MasjidApp.Core.Events;
using Xunit;

namespace MasjidApp.Core.Tests.Events;

public sealed class EventAlertTextTests
{
    [Theory]
    [InlineData("The event is canceled.", true)]
    [InlineData("CANCELLED: Friday's event", true)]
    [InlineData("Tonight's event was (Cancelled).", true)]
    [InlineData("A previously-canceled class", true)]
    [InlineData("The event was uncanceled.", false)]
    [InlineData("The event may be rescheduled or cancelledLater.", false)]
    [InlineData("Please use the east entrance.", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IndicatesCancellation_WithAlertText_MatchesWholeCancellationWords(string? text, bool expected)
    {
        EventAlertText.IndicatesCancellation(text).Should().Be(expected);
    }
}
