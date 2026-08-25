namespace MasjidApp.Core.Utils;

/// <summary>Determines where donation-page navigation is allowed to open.</summary>
public static class DonationNavigationPolicy
{
    /// <summary>Returns an absolute HTTPS donation URI, or <c>null</c> for invalid input.</summary>
    public static Uri? GetSafeDonationUri(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            return null;
        }

        return uri;
    }

    /// <summary>
    /// Keeps same-origin HTTPS navigation in the embedded browser, sends other HTTPS
    /// origins to the system browser, and blocks every other destination.
    /// </summary>
    public static DonationNavigationAction GetNavigationAction(
        Uri? donationUri,
        string? navigationUrl)
    {
        if (donationUri is null ||
            GetSafeDonationUri(donationUri.ToString()) is null ||
            GetSafeDonationUri(navigationUrl) is not { } navigationUri)
        {
            return DonationNavigationAction.Block;
        }

        return donationUri.IdnHost.Equals(navigationUri.IdnHost, StringComparison.OrdinalIgnoreCase) &&
               donationUri.Port == navigationUri.Port
            ? DonationNavigationAction.OpenInWebView
            : DonationNavigationAction.OpenInSystemBrowser;
    }
}

/// <summary>Available destinations for a donation-page navigation request.</summary>
public enum DonationNavigationAction
{
    /// <summary>Cancel navigation.</summary>
    Block,

    /// <summary>Continue navigation in the embedded WebView.</summary>
    OpenInWebView,

    /// <summary>Cancel embedded navigation and open it in the system-preferred browser.</summary>
    OpenInSystemBrowser,
}