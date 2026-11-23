namespace MasjidApp.Core.Configuration;

/// <summary>The concrete platform icon identifiers an <see cref="AppIconPreference"/> resolves to.</summary>
/// <param name="IosAlternateIconName">iOS <c>CFBundleAlternateIcons</c> name, or <c>null</c> for the primary icon.</param>
/// <param name="AndroidLauncherAlias">The Android launcher activity alias to enable; all others must be disabled.</param>
public sealed record ResolvedAppIcon(string? IosAlternateIconName, string AndroidLauncherAlias);

/// <summary>
/// Maps the persisted icon preference onto the compiled platform icon assets
/// (<c>Info.plist</c> alternate icons and <c>AndroidManifest.xml</c> launcher aliases).
/// </summary>
public static class AppIconResolver
{
    /// <summary>iOS alternate icon name of the MasjidFeed light icon.</summary>
    public const string IosLightIconName = "MASJIDFEED_LIGHT";

    /// <summary>Android launcher alias of the MasjidFeed light icon.</summary>
    public const string AndroidLightLauncherAlias = ".MasjidFeedLightLauncher";

    /// <summary>Android launcher alias of the default MasjidFeed dark icon.</summary>
    public const string AndroidDefaultLauncherAlias = ".GenericLauncher";

    /// <summary>Resolves <paramref name="preference"/> against the active tenant branding.</summary>
    public static ResolvedAppIcon Resolve(AppIconPreference preference, TenantBrandingDefinition? branding) =>
        preference switch
        {
            AppIconPreference.Light => new(IosLightIconName, AndroidLightLauncherAlias),
            AppIconPreference.Masjid when branding is not null => new(
                branding.IosAlternateIconName,
                $".{branding.AndroidLauncherAlias}"),
            _ => new(null, AndroidDefaultLauncherAlias),
        };
}
