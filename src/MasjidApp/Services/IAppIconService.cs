using MasjidApp.Core.Configuration;

namespace MasjidApp.Services;

/// <summary>
/// Switches between the MasjidFeed icons and tenant-specific launcher icons
///. The selected <see cref="AppIconPreference"/> is persisted and
/// re-applied on every launch and masjid switch.
/// </summary>
public interface IAppIconService
{
    /// <summary>Gets the persisted icon preference.</summary>
    AppIconPreference Current { get; }

    /// <summary>Persists <paramref name="preference"/> and applies the icon it resolves to.</summary>
    Task ApplyAsync(AppIconPreference preference, TenantBrandingDefinition? branding);
}
