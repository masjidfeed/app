namespace MasjidApp.Core.Configuration;

/// <summary>Resolves and validates manual or QR-scanned masjid configuration URLs.</summary>
public static class MasjidConfigUrl
{
    private const string ConfigPath = "/wp-json/masjid/v1/config";

    /// <summary>Resolves a website or complete config URL to the canonical config endpoint.</summary>
    public static string? Resolve(string? input)
    {
        if (!Uri.TryCreate(input?.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (uri.AbsolutePath.Contains("/config", StringComparison.OrdinalIgnoreCase))
        {
            return uri.ToString();
        }

        return new Uri(uri, ConfigPath).ToString();
    }

    /// <summary>Returns the website origin used to verify that the submitted site is online.</summary>
    public static string? ResolveWebsite(string? input)
    {
        if (!Uri.TryCreate(input?.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        return uri.AbsolutePath.Contains("/config", StringComparison.OrdinalIgnoreCase)
            ? new Uri(uri.GetLeftPart(UriPartial.Authority) + "/").ToString()
            : uri.ToString();
    }

    /// <summary>Allows HTTPS everywhere and HTTP only for emulator/local-development hosts.</summary>
    public static bool IsAllowed(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            return true;
        }

        return uri.Scheme == Uri.UriSchemeHttp &&
               uri.Host is "localhost" or "10.0.2.2" or "127.0.0.1";
    }
}
