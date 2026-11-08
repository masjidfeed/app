using MasjidApp.Services;
using Microsoft.Maui.ApplicationModel;

namespace MasjidApp.PlatformPermissions;

// Launches the OS page where the app's notifications can be enabled (FR-PN-03).

/// <summary>Default no-op implementation for platforms without a dedicated settings page.</summary>
#if !ANDROID && !IOS
public sealed class DeviceNotificationSettings : IDeviceNotificationSettings
{
    /// <inheritdoc />
    public Task OpenAsync() => Task.CompletedTask;
}
#elif ANDROID
public sealed class DeviceNotificationSettings : IDeviceNotificationSettings
{
    /// <inheritdoc />
    public Task OpenAsync()
    {
        var activity = Platform.CurrentActivity
            ?? throw new InvalidOperationException("No current Android activity is available.");

        var intent = new global::Android.Content.Intent(
            global::Android.Provider.Settings.ActionAppNotificationSettings);
        intent.PutExtra(
            global::Android.Provider.Settings.ExtraAppPackage,
            activity.PackageName);
        intent.AddFlags(global::Android.Content.ActivityFlags.NewTask);
        activity.StartActivity(intent);
        return Task.CompletedTask;
    }
}
#else
public sealed class DeviceNotificationSettings : IDeviceNotificationSettings
{
    /// <inheritdoc />
    public Task OpenAsync()
    {
        var settingsUrl = UIKit.UIApplication.OpenSettingsUrlString;
        if (!string.IsNullOrEmpty(settingsUrl) &&
            Foundation.NSUrl.FromString(settingsUrl) is { } url)
        {
            UIKit.UIApplication.SharedApplication.OpenUrl(url, new UIKit.UIApplicationOpenUrlOptions(), null);
        }

        return Task.CompletedTask;
    }
}
#endif
