#if ANDROID
using Android.Content;
using Android.Content.PM;
#elif IOS
using UIKit;
#endif
using MasjidApp.Core.Configuration;

namespace MasjidApp.Services;

/// <inheritdoc/>
public sealed class AppIconService : IAppIconService
{
    private const string PreferenceKey = "app_icon_preference";

    private readonly IUserPreferences _preferences;

    public AppIconService(IUserPreferences preferences) => _preferences = preferences;

    /// <inheritdoc/>
    public AppIconPreference Current
    {
        get
        {
            var stored = _preferences.GetValue(PreferenceKey, (int)AppIconPreference.Masjid);
            return Enum.IsDefined(typeof(AppIconPreference), stored)
                ? (AppIconPreference)stored
                : AppIconPreference.Masjid;
        }
    }

    /// <inheritdoc/>
    public Task ApplyAsync(AppIconPreference preference, TenantBrandingDefinition? branding)
    {
        _preferences.SetValue(PreferenceKey, (int)preference);
        var icon = AppIconResolver.Resolve(preference, branding);

#if ANDROID
        var context = Android.App.Application.Context;
        var packageManager = context.PackageManager;
        var packageName = context.PackageName;
        if (packageManager is null || string.IsNullOrWhiteSpace(packageName))
        {
            return Task.CompletedTask;
        }

        // Enable exactly the resolved alias and disable every other launcher alias,
        // including the ones compiled for tenants the user has never selected.
        var aliases = new List<string>
        {
            AppIconResolver.AndroidDefaultLauncherAlias,
            AppIconResolver.AndroidLightLauncherAlias,
        };
        aliases.AddRange(TenantBranding.All.Select(definition => $".{definition.AndroidLauncherAlias}"));
        foreach (var alias in aliases)
        {
            SetAlias(packageManager, packageName, alias, alias == icon.AndroidLauncherAlias);
        }
#elif IOS
        if (UIApplication.SharedApplication.SupportsAlternateIcons)
        {
            UIApplication.SharedApplication.SetAlternateIconName(
                icon.IosAlternateIconName,
                static _ => { });
        }
#endif
        return Task.CompletedTask;
    }

#if ANDROID
    private static void SetAlias(
        PackageManager packageManager,
        string packageName,
        string alias,
        bool enabled)
    {
        var component = new ComponentName(packageName, packageName + alias);
        packageManager.SetComponentEnabledSetting(
            component,
            enabled ? ComponentEnabledState.Enabled : ComponentEnabledState.Disabled,
            ComponentEnableOption.DontKillApp);
    }
#endif
}
