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
    /// <inheritdoc/>
    public Task ApplyAsync(TenantBrandingDefinition? branding)
    {
#if ANDROID
        var context = Android.App.Application.Context;
        var packageManager = context.PackageManager;
        var packageName = context.PackageName;
        if (packageManager is null || string.IsNullOrWhiteSpace(packageName))
        {
            return Task.CompletedTask;
        }

        SetAlias(packageManager, packageName, ".GenericLauncher", branding is null);
        foreach (var definition in TenantBranding.All)
        {
            SetAlias(
            packageManager,
            packageName,
            $".{definition.AndroidLauncherAlias}",
            definition == branding);
        }
#elif IOS
        if (UIApplication.SharedApplication.SupportsAlternateIcons)
        {
            UIApplication.SharedApplication.SetAlternateIconName(
            branding?.IosAlternateIconName,
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
