using MasjidApp.Core.Utils;
using MasjidApp.Localization;
using MasjidApp.ViewModels;

namespace MasjidApp.Views;

// FR-DN-01–FR-DN-04, NFR-SC-03: donations link page.

/// <summary>
/// Donations page - renders the masjid's configured HTTPS donation page inline
/// and opens other HTTPS origins in the system-preferred browser. No payment or
/// card data is ever stored or processed by this app (NFR-SC-03).
/// The Donate tab is hidden by batch F1 navigation logic when no URL is
/// configured (FR-DN-03); this page still shows a graceful fallback message
/// in that case.
/// </summary>
public partial class DonationsPage : ContentPage
{
    private readonly DonationsViewModel _viewModel;

    /// <summary>
    /// Creates the page with its injected ViewModel.
    /// </summary>
    public DonationsPage(DonationsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    /// <inheritdoc/>
    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (_viewModel.LoadCommand.CanExecute(null))
        {
            _viewModel.LoadCommand.Execute(null);
        }
    }

    /// <inheritdoc/>
    protected override bool OnBackButtonPressed()
    {
        if (DonationWebView.IsVisible && DonationWebView.CanGoBack)
        {
            DonationWebView.GoBack();
            return true;
        }

        return base.OnBackButtonPressed();
    }

    private async void OnDonationWebViewNavigating(object? sender, WebNavigatingEventArgs e)
    {
        var donationUri = DonationNavigationPolicy.GetSafeDonationUri(_viewModel.DonationUrl);
        var action = DonationNavigationPolicy.GetNavigationAction(donationUri, e.Url);

        if (action == DonationNavigationAction.OpenInWebView)
        {
            DonationErrorPanel.IsVisible = false;
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
                await DisplayAlertAsync(
                    LocalizationManager.Instance["Donations_BrowserError_Title"],
                    LocalizationManager.Instance["Donations_BrowserError_Message"],
                    LocalizationManager.Instance["App_OK"]);
            }
        }
    }

    private async void OnDonationWebViewNavigated(object? sender, WebNavigatedEventArgs e)
    {
        SetWebLoading(false);
        DonationErrorPanel.IsVisible = e.Result is not WebNavigationResult.Success
            and not WebNavigationResult.Cancel;

        if (e.Result == WebNavigationResult.Success)
        {
            await ApplyDonationThemeAsync();
        }
    }

    private async Task ApplyDonationThemeAsync()
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
            await DonationWebView.EvaluateJavaScriptAsync(script);
        }
        catch (Exception)
        {
            // Navigation or WebView process termination can race script evaluation.
        }
    }

    private void OnDonationWebViewProcessTerminated(
        object? sender,
        WebViewProcessTerminatedEventArgs e)
    {
        SetWebLoading(false);
        DonationErrorPanel.IsVisible = true;
    }

    private void OnRetryClicked(object? sender, EventArgs e)
    {
        DonationErrorPanel.IsVisible = false;
        SetWebLoading(true);
        DonationWebView.Reload();
    }

    private void SetWebLoading(bool isLoading)
    {
        DonationLoadingIndicator.IsRunning = isLoading;
        DonationLoadingIndicator.IsVisible = isLoading;
    }
}
