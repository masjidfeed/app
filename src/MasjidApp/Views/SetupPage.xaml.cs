using System.ComponentModel;
using MasjidApp.Core.Configuration;
using MasjidApp.Core.Services;
using MasjidApp.Services;
using MasjidApp.ViewModels;

namespace MasjidApp.Views;

// F2: Full-screen blocking first-run setup - shown when no masjid is configured.
// FR-ST-01, FR-ST-06, API-10.

/// <summary>
/// Full-screen, mandatory first-run setup page shown when <see cref="IMasjidContext.ActiveEntry"/>
/// is <c>null</c>.  Reuses <see cref="AddMasjidViewModel"/> for URL validation and QR logic.
/// <para>
/// Cannot be bypassed: the Android hardware back button is suppressed, and the page is set
/// as the window root (not presented modally), so there is no swipe-to-dismiss gesture.
/// Transitions to the normal <see cref="AppShell"/> as soon as a masjid becomes active.
/// </para>
/// </summary>
public partial class SetupPage : ContentPage
{
    private const string LearnMoreUrl = "https://goodsoftware.foundation/p/masjidfeed";

    private readonly IServiceProvider _services;
    private readonly IMasjidContext _masjidContext;
    private readonly AddMasjidViewModel _viewModel;
    private readonly IQrScannerService _qrScanner;
    private readonly IAppIconService _appIconService;
    private readonly IContactActionService _contactActionService;

    /// <summary>Creates the setup page with DI-resolved dependencies.</summary>
    public SetupPage(
        AddMasjidViewModel viewModel,
        IMasjidContext masjidContext,
        IServiceProvider services,
        IQrScannerService qrScanner,
        IAppIconService appIconService,
        IContactActionService contactActionService)
    {
        _services = services;
        _masjidContext = masjidContext;
        _viewModel = viewModel;
        _qrScanner = qrScanner;
        _appIconService = appIconService;
        _contactActionService = contactActionService;
        InitializeComponent();
        BindingContext = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // Transition to AppShell once a masjid becomes active (API-10).
        _masjidContext.ActiveMasjidChanged += OnActiveMasjidChanged;
    }

    /// <summary>
    /// Blocks the Android hardware back button - the setup screen is mandatory (F2).
    /// </summary>
    protected override bool OnBackButtonPressed() => true;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AddMasjidViewModel.ShowUnsupportedWebsite) &&
            _viewModel.ShowUnsupportedWebsite)
        {
            _ = ScrollToUnsupportedWebsiteAsync();
        }
    }

    private async Task ScrollToUnsupportedWebsiteAsync()
    {
        await Task.Delay(100);
        await MainThread.InvokeOnMainThreadAsync(() =>
            SetupScrollView.ScrollToAsync(0, UnsupportedWebsitePanel.Y, animated: true));
    }

    private void OnActiveMasjidChanged(object? sender, EventArgs e)
    {
        if (_masjidContext.ActiveEntry is null)
        {
            return;
        }

        // Unsubscribe immediately to avoid firing again on later masjid switches once
        // the shell is already the window root.
        _masjidContext.ActiveMasjidChanged -= OnActiveMasjidChanged;

        // Swap window root to AppShell on the UI thread (API-06).
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            var branding = TenantBranding.Get(_masjidContext.ActiveEntry);
            await _appIconService.ApplyAsync(branding);
            if (Application.Current?.Windows is { Count: > 0 } windows)
            {
                windows[0].Page = branding is not null
                    ? _services.GetRequiredService<BrandedSplashPage>()
                    : _services.GetRequiredService<AppShell>();
            }
        });
    }

    /// <summary>
    /// Opens the device camera for QR scanning (FR-ST-06, FR-CM-03).
    /// Degrades gracefully if camera permission is denied.
    /// </summary>
    private async void OnScanQrClicked(object? sender, EventArgs e)
    {
        var value = await _qrScanner.ScanAsync(this);
        if (!string.IsNullOrWhiteSpace(value))
        {
            _viewModel.Url = value;
        }
    }

    private async void OnLearnMoreClicked(object? sender, EventArgs e) =>
        await _contactActionService.OpenBrowserAsync(LearnMoreUrl);
}
