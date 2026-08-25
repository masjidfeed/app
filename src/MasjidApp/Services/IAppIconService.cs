using MasjidApp.Core.Configuration;

namespace MasjidApp.Services;

/// <summary>Switches between generic and tenant-specific launcher icons.</summary>
public interface IAppIconService
{
    /// <summary>Applies the active tenant's icon or restores the generic icon.</summary>
    Task ApplyAsync(TenantBrandingDefinition? branding);
}
