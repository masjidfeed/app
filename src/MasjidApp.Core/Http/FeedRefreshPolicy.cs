namespace MasjidApp.Core.Http;

// FR-CM-01, FR-EV-01, API-09, NFR-OF-01, NFR-RL-01

/// <summary>
/// Rules that keep list-style feeds (announcements, events) populated across app
/// resumes: a load that produced no data never blanks content already on screen.
/// </summary>
public static class FeedRefreshPolicy
{
    /// <summary>
    /// Determines whether a load result should overwrite the feed. Results without a
    /// value (network failure with no usable cache) leave the previously rendered items
    /// in place rather than replacing them with an empty state (API-09, NFR-RL-01).
    /// </summary>
    /// <typeparam name="T">The payload type carried by the result.</typeparam>
    /// <param name="result">The load result, or <c>null</c> when no request was made.</param>
    /// <returns><c>true</c> when the result carries data the feed should render.</returns>
    public static bool ShouldReplaceItems<T>(ApiResult<T>? result)
        where T : class =>
        result?.HasValue == true;

}
