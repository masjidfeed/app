namespace MasjidApp.Core.Push;

/// <summary>The post feed targeted by a push-notification deep link.</summary>
public enum PostDeepLinkType
{
    /// <summary>An announcement post.</summary>
    Announcements,

    /// <summary>An event post.</summary>
    Events,
}

/// <summary>A validated <c>masjidapp://open/v1</c> post deep link.</summary>
/// <param name="MasjidId">Tenant identifier carried by the notification.</param>
/// <param name="Type">Target post feed.</param>
/// <param name="PostId">Positive WordPress post identifier.</param>
public sealed record PostDeepLink(string MasjidId, PostDeepLinkType Type, int PostId)
{
    private const string Scheme = "masjidapp";
    private const string Host = "open";

    /// <summary>Parses the versioned custom URI contract documented by the Masjid API.</summary>
    public static bool TryParse(Uri? uri, out PostDeepLink? deepLink)
    {
        deepLink = null;
        if (uri is null || !uri.IsAbsoluteUri ||
            !string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.Host, Host, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments is not ["v1", "masjids", var encodedMasjidId, var typeValue, var postIdValue] ||
            !int.TryParse(postIdValue, out var postId) || postId <= 0)
        {
            return false;
        }

        var masjidId = Uri.UnescapeDataString(encodedMasjidId);
        if (string.IsNullOrWhiteSpace(masjidId))
        {
            return false;
        }

        var type = typeValue.ToLowerInvariant() switch
        {
            "announcements" => PostDeepLinkType.Announcements,
            "events" => PostDeepLinkType.Events,
            _ => (PostDeepLinkType?)null,
        };
        if (type is null)
        {
            return false;
        }

        deepLink = new PostDeepLink(masjidId, type.Value, postId);
        return true;
    }
}