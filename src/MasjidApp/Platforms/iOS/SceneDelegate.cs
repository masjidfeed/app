using Foundation;
using MasjidApp.Push;
using Microsoft.Extensions.DependencyInjection;
using UIKit;

namespace MasjidApp;

[Register("SceneDelegate")]
public class SceneDelegate : MauiUISceneDelegate
{
    public override void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions connectionOptions)
    {
        base.WillConnect(scene, session, connectionOptions);

        if (connectionOptions.UrlContexts is { } urlContexts)
        {
            RouteUrls(urlContexts);
        }
    }

    public override bool OpenUrl(UIScene scene, NSSet<UIOpenUrlContext> urlContexts)
    {
        var handled = base.OpenUrl(scene, urlContexts);
        return RouteUrls(urlContexts) || handled;
    }

    private static bool RouteUrls(NSSet<UIOpenUrlContext> urlContexts)
    {
        var handled = false;
        var router = IPlatformApplication.Current?.Services.GetService<IDeepLinkRouter>();
        foreach (var context in urlContexts)
        {
            if (router is not null && Uri.TryCreate(context.Url.AbsoluteString, UriKind.Absolute, out var uri))
            {
                _ = router.RouteAsync(uri);
                handled = true;
            }
        }

        return handled;
    }
}
