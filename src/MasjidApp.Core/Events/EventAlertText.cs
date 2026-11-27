using System.Text.RegularExpressions;

namespace MasjidApp.Core.Events;

/// <summary>Recognizes cancellation wording in an event alert.</summary>
public static partial class EventAlertText
{
    /// <summary>Checks for either English spelling of canceled as a whole word.</summary>
    public static bool IndicatesCancellation(string? text) =>
        text is not null && CancellationWord().IsMatch(text);

    [GeneratedRegex(@"\bcancell?ed\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CancellationWord();
}
