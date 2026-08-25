using MasjidApp.Core.Models;

namespace MasjidApp.Core.Configuration;

// API-10: per-masjid endpoint URLs may be supplied relative to the masjid's own
// config-document origin. Resolve them to absolute URLs against that origin so the
// HTTP client never falls back to the (wrong) environment base URL for relative paths.

/// <summary>
/// Resolves per-masjid endpoint URLs that may be relative to the masjid's config
/// document into absolute URLs.
/// <list type="bullet">
/// <item>Absolute <c>http</c>/<c>https</c> URLs are returned unchanged.</item>
/// <item>Protocol-relative URLs (<c>//host/path</c>) adopt the config URL's scheme.</item>
/// <item>Root-relative (<c>/path</c>) and path-relative (<c>path</c>) URLs are resolved
/// against the config URL per RFC 3986.</item>
/// </list>
/// Values that cannot be resolved (e.g. an invalid base) are returned unchanged.
/// </summary>
public static class EndpointUrlResolver
{
    /// <summary>
    /// Resolves a single, possibly-relative <paramref name="endpointUrl"/> against the
    /// absolute <paramref name="configUrl"/>. Returns <paramref name="endpointUrl"/>
    /// unchanged when it is null/blank, already absolute, or cannot be resolved.
    /// </summary>
    public static string? Resolve(string? endpointUrl, string? configUrl)
    {
        if (string.IsNullOrWhiteSpace(endpointUrl))
        {
            return endpointUrl;
        }

        // Already an absolute http(s) URL - use as-is.
        if (Uri.TryCreate(endpointUrl, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            return endpointUrl;
        }

        // Need a valid absolute base to resolve a relative endpoint against.
        if (string.IsNullOrWhiteSpace(configUrl) ||
            !Uri.TryCreate(configUrl, UriKind.Absolute, out var baseUri))
        {
            return endpointUrl;
        }

        // Protocol-relative ("//host/path"): adopt the base URL's scheme.
        if (endpointUrl.StartsWith("//", StringComparison.Ordinal))
        {
            return Uri.TryCreate($"{baseUri.Scheme}:{endpointUrl}", UriKind.Absolute, out var protocolRelative)
                ? protocolRelative.ToString()
                : endpointUrl;
        }

        // Root-relative ("/path") or path-relative ("path"): standard RFC 3986 resolution.
        return Uri.TryCreate(baseUri, endpointUrl, out var resolved)
            ? resolved.ToString()
            : endpointUrl;
    }

    /// <summary>
    /// Resolves a single, possibly-relative <paramref name="url"/> against the origin
    /// of the absolute <paramref name="configUrl"/>. Use this for masjid content URLs
    /// such as branding, contact, social, and image links that are relative to the
    /// WordPress site root rather than to the config document path.
    /// </summary>
    public static string? ResolveAgainstOrigin(string? url, string? configUrl)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            return url;
        }

        if (string.IsNullOrWhiteSpace(configUrl) ||
            !Uri.TryCreate(configUrl, UriKind.Absolute, out var configUri))
        {
            return null;
        }

        if (url.StartsWith("//", StringComparison.Ordinal))
        {
            return Uri.TryCreate($"{configUri.Scheme}:{url}", UriKind.Absolute, out var protocolRelative)
                ? protocolRelative.ToString()
                : null;
        }

        var origin = new Uri(configUri.GetLeftPart(UriPartial.Authority));
        return Uri.TryCreate(origin, url, out var resolved)
            ? resolved.ToString()
            : null;
    }

    /// <summary>
    /// Returns a copy of <paramref name="endpoints"/> with every endpoint URL resolved
    /// against <paramref name="configUrl"/>. Returns <c>null</c> when
    /// <paramref name="endpoints"/> is <c>null</c>.
    /// </summary>
    public static EndpointsConfig? Resolve(EndpointsConfig? endpoints, string? configUrl)
    {
        if (endpoints is null)
        {
            return null;
        }

        return endpoints with
        {
            SalahApiUrl = Resolve(endpoints.SalahApiUrl, configUrl),
            EventsUrl = Resolve(endpoints.EventsUrl, configUrl),
            AnnouncementsUrl = Resolve(endpoints.AnnouncementsUrl, configUrl),
        };
    }

    /// <summary>
    /// Returns a copy of <paramref name="config"/> with its <see cref="EndpointsConfig"/>
    /// URLs resolved against <paramref name="configUrl"/> (the URL the document was
    /// fetched from).
    /// </summary>
    public static MasjidConfig Resolve(MasjidConfig config, string? configUrl)
    {
        ArgumentNullException.ThrowIfNull(config);
        return config with
        {
            Endpoints = Resolve(config.Endpoints, configUrl),
            PushNotifications = config.PushNotifications is null
                ? null
                : config.PushNotifications with
                {
                    RegistrationUrl = Resolve(config.PushNotifications.RegistrationUrl, configUrl),
                },
        };
    }
}
