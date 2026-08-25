using MasjidApp.Core.Models;

namespace MasjidApp.Core.Configuration;

/// <summary>Identifies tenants that have baked-in native branding assets.</summary>
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
            if (entry.Id.Equals(definition.Id, StringComparison.OrdinalIgnoreCase) &&
                IsHostedBy(configUri, definition.WebsiteHost))
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
