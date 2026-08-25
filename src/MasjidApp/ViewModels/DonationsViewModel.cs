using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasjidApp.Core.Services;
using MasjidApp.Core.Utils;

namespace MasjidApp.ViewModels;

// FR-DN-01, FR-DN-02, FR-DN-03, FR-DN-04, NFR-SC-03:
// Opens a masjid-provided HTTPS donation URL - never processes payment data.

/// <summary>
/// ViewModel for the Donations screen.
/// Loads the <c>donationUrl</c> from the active masjid config and opens it in
/// the system browser. No payment or card data is ever stored or processed
/// in the client.
/// </summary>
public sealed partial class DonationsViewModel : ViewModelBase
{
    private readonly IMasjidContext _context;

    /// <summary>
    /// The configured donation URL, or <c>null</c> when not set.
    /// Must be HTTPS to be considered valid (<see cref="HasDonationUrl"/>).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDonationUrl))]
    [NotifyPropertyChangedFor(nameof(ShowNoDonationUrl))]
    [NotifyCanExecuteChangedFor(nameof(OpenDonationsCommand))]
    private string? _donationUrl;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoDonationUrl))]
    private bool _hasLoaded;

    /// <summary>Masjid display name shown on the screen.</summary>
    [ObservableProperty]
    private string _masjidName = string.Empty;

    /// <summary>
    /// <c>true</c> when a non-empty, well-formed HTTPS donation URL is configured.
    /// When <c>false</c> the Donate button is hidden (FR-DN-03, FR-DN-04).
    /// </summary>
    public bool HasDonationUrl =>
        DonationNavigationPolicy.GetSafeDonationUri(DonationUrl) is not null;

    /// <summary>Whether loading completed without producing a valid donation URL.</summary>
    public bool ShowNoDonationUrl => HasLoaded && !HasDonationUrl;

    /// <summary>
    /// Creates the ViewModel with its required dependencies.
    /// </summary>
    public DonationsViewModel(IMasjidContext context)
    {
        _context = context;
        Title = "Donate";
    }

    /// <summary>
    /// Loads the donation URL and masjid name from the active configuration.
    /// Called on page appearing.
    /// </summary>
    [RelayCommand]
    private async Task LoadAsync(CancellationToken ct)
    {
        IsBusy = true;
        HasLoaded = false;
        try
        {
            var result = await _context.GetConfigAsync(ct: ct);
            var config = result?.ValueOrDefault;

            MasjidName = config?.Masjid?.Name ?? string.Empty;
            DonationUrl = config?.DonationUrl;
        }
        finally
        {
            IsBusy = false;
            HasLoaded = true;
        }
    }

    /// <summary>
    /// Opens the donation URL in the system browser.
    /// NFR-SC-03: URL is validated to be HTTPS before opening.
    /// No payment or card data is processed by the client.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasDonationUrl))]
    private async Task OpenDonationsAsync()
    {
        if (DonationNavigationPolicy.GetSafeDonationUri(DonationUrl) is not { } uri)
        {
            return;
        }

        await Browser.Default
            .OpenAsync(uri, BrowserLaunchMode.SystemPreferred);
    }
}
