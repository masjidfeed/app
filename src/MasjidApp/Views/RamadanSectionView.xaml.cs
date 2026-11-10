using MasjidApp.Core.Utils;
using MasjidApp.ViewModels;

namespace MasjidApp.Views;

// API-06 ramadan feature flag: inline Ramadan page hosted by the Home shell's
// "Ramadan" category, using the same navigation policy as the Donations page.

/// <summary>
/// Ramadan section - renders the masjid's configured HTTPS Ramadan page inline
/// and opens other HTTPS origins in the system-preferred browser. The section
/// is only reachable when the ramadan feature flag and ramadanUrl are set.
/// The <see cref="RamadanViewModel"/> arrives via BindingContext from the
/// Home shell carousel template.
/// </summary>
public partial class RamadanSectionView : ContentView
{
    private RamadanViewModel? ViewModel => BindingContext as RamadanViewModel;

    public RamadanSectionView()
    {
        InitializeComponent();

        // The Home shell carousel assigns the template ROOT's BindingContext directly
        // to the item (HomeShellCategoryPage). Whether this view's own markup
        // BindingContext binding then evaluates correctly depends on the order of
        // that stomp vs. template-content binding initialization; when the item is
        // materialized during the CategoryPages rebuild (first config load), the
        // binding can be evaluated while detached and never re-evaluated, leaving
        // the section blank (every child binding dead). Resolve the VM
        // deterministically instead: on every context/parent change, if this view
        // does not hold the RamadanViewModel, take it from the shell's VM.
        BindingContextChanged += (_, _) => ResolveBindingContext();
        ParentChanged += (_, _) => ResolveBindingContext();
    }

    private void ResolveBindingContext()
    {
        if (BindingContext is RamadanViewModel)
        {
            return;
        }

        for (Element? element = Parent; element is not null; element = element.Parent)
        {
            if (element.BindingContext is HomeShellViewModel shell)
            {
                BindingContext = shell.Ramadan;
                return;
            }
        }
    }

    private async void OnRamadanWebViewNavigating(object? sender, WebNavigatingEventArgs e)
    {
        var viewModel = ViewModel;
        if (viewModel is null)
        {
            return;
        }

        var ramadanUri = DonationNavigationPolicy.GetSafeDonationUri(viewModel.RamadanUrl);
        var action = DonationNavigationPolicy.GetNavigationAction(ramadanUri, e.Url);

        if (action == DonationNavigationAction.OpenInWebView)
        {
            RamadanErrorPanel.IsVisible = false;
            SetWebLoading(true);
            return;
        }

        e.Cancel = true;
        SetWebLoading(false);

        if (action == DonationNavigationAction.OpenInSystemBrowser &&
            DonationNavigationPolicy.GetSafeDonationUri(e.Url) is { } externalUri)
        {
            try
            {
                await Browser.Default.OpenAsync(externalUri, BrowserLaunchMode.SystemPreferred);
            }
            catch (Exception)
            {
                // Surface the failure through the in-section error panel, which
                // still offers Retry and Open-in-Browser actions.
                RamadanErrorPanel.IsVisible = true;
            }
        }
    }

    private async void OnRamadanWebViewNavigated(object? sender, WebNavigatedEventArgs e)
    {
        SetWebLoading(false);
        RamadanErrorPanel.IsVisible = e.Result is not WebNavigationResult.Success
            and not WebNavigationResult.Cancel;

        if (e.Result == WebNavigationResult.Success)
        {
            await ApplyRamadanThemeAsync();
        }
    }

    private async Task ApplyRamadanThemeAsync()
    {
        var scheme = Application.Current?.RequestedTheme == AppTheme.Dark ? "dark" : "light";
        var script = $$"""
            (() => {
                const scheme = '{{scheme}}';
                const root = document.documentElement;
                root.dataset.colorScheme = scheme;
                root.style.colorScheme = scheme;
            })();
            """;

        try
        {
            await RamadanWebView.EvaluateJavaScriptAsync(script);
        }
        catch (Exception)
        {
            // Navigation or WebView process termination can race script evaluation.
        }
    }

    private void OnRamadanWebViewProcessTerminated(
        object? sender,
        WebViewProcessTerminatedEventArgs e)
    {
        SetWebLoading(false);
        RamadanErrorPanel.IsVisible = true;
    }

    private void OnRetryClicked(object? sender, EventArgs e)
    {
        RamadanErrorPanel.IsVisible = false;
        SetWebLoading(true);
        RamadanWebView.Reload();
    }

    private void SetWebLoading(bool isLoading)
    {
        RamadanLoadingIndicator.IsRunning = isLoading;
        RamadanLoadingIndicator.IsVisible = isLoading;
    }
}
