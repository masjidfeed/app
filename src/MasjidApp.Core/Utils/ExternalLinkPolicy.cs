namespace MasjidApp.Core.Utils;

/// <summary>Validates remote links before the app hands them to a system browser.</summary>
public static class ExternalLinkPolicy
{
    /// <summary>Returns an absolute HTTP(S) URI, or <c>null</c> for every other input.</summary>
    public static Uri? GetSafeUri(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return null;
        }

        return uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
               uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            ? uri
            : null;
    }
}
