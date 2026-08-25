using System.Diagnostics.CodeAnalysis;
using Foundation;
using MasjidApp.Push;
using MasjidApp.Widgets;
using Microsoft.Extensions.DependencyInjection;
using UIKit;

namespace MasjidApp;

// C2: FR-PN-01, FR-PN-02 - APNs token delivery.

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    /// <summary>
    /// Registers the widget background-refresh task during launch.  D1, FR-WG-04:
    /// <c>BGTaskScheduler</c> requires handlers to be registered before launch
    /// completes, so this runs immediately after the MAUI app is built.
    /// </summary>
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Objective-C delegate callbacks must be instance methods for selector dispatch.")]
    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        var result = base.FinishedLaunching(application, launchOptions);
        Platforms.iOS.UnifiedNotificationCenterDelegate.Install();

        var scheduler = IPlatformApplication.Current?.Services
            .GetService<IBackgroundRefreshScheduler>();
        scheduler?.Register();
        scheduler?.Schedule();

        return result;
    }

    public override bool OpenUrl(UIApplication application, NSUrl url, NSDictionary options)
    {
        if (Uri.TryCreate(url.AbsoluteString, UriKind.Absolute, out var uri) &&
            IPlatformApplication.Current?.Services.GetService<IDeepLinkRouter>() is { } router)
        {
            _ = router.RouteAsync(uri);
            return true;
        }

        return base.OpenUrl(application, url, options);
    }

}
