using MasjidApp.Core.Models;

namespace MasjidApp.Core.Configuration;

/// <summary>Identifies tenants that have baked-in native branding assets.</summary>
/// <remarks>
/// Matching is by the config document's website host only: WordPress generates each
/// installation's masjid id (e.g. "masjid-312fc341"), so it is not knowable when the
/// native branding is compiled, while the website domain is the stable identity of a
/// pre-configured masjid app.
/// </remarks>
public static partial class TenantBranding
{
    /// <summary>Returns the matching native branding definition for the active tenant.</summary>
    public static TenantBrandingDefinition? Get(MasjidStoredEntry? entry)
    {
        if (entry is null ||
            !Uri.TryCreate(entry.ConfigUrl, UriKind.Absolute, out var configUri))
        {
            return null;
        }

        foreach (var definition in Definitions)
        {
            if (IsHostedBy(configUri, definition.WebsiteHost))
            {
                return definition;
            }
        }

        return null;
    }

    private static bool IsHostedBy(Uri configUri, string websiteHost) =>
        configUri.Host.Equals(websiteHost, StringComparison.OrdinalIgnoreCase) ||
        configUri.Host.EndsWith($".{websiteHost}", StringComparison.OrdinalIgnoreCase);
}
