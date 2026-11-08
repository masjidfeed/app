using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasjidApp.Core.Services;
using MasjidApp.Core.Utils;

namespace MasjidApp.ViewModels;

// API-06: Ramadan section rendered inline from the masjid-provided HTTPS ramadanUrl,
// mirroring the Donations page navigation policy.

/// <summary>
/// ViewModel for the Ramadan home category. Loads the <c>ramadanUrl</c> from the
/// active masjid config; the page renders it inline like the Donations page.
/// </summary>
public sealed partial class RamadanViewModel : ViewModelBase
{
    private readonly IMasjidContext _context;

    /// <summary>
    /// The configured Ramadan URL, or <c>null</c> when not set.
    /// Must be HTTPS to be considered valid (<see cref="HasRamadanUrl"/>).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRamadanUrl))]
    [NotifyPropertyChangedFor(nameof(ShowNoRamadanUrl))]
    [NotifyCanExecuteChangedFor(nameof(OpenRamadanCommand))]
    private string? _ramadanUrl;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoRamadanUrl))]
    private bool _hasLoaded;

    /// <summary>
    /// <c>true</c> when a non-empty, well-formed HTTPS Ramadan URL is configured.
    /// </summary>
    public bool HasRamadanUrl =>
        DonationNavigationPolicy.GetSafeDonationUri(RamadanUrl) is not null;

    /// <summary>Whether loading completed without producing a valid Ramadan URL.</summary>
    public bool ShowNoRamadanUrl => HasLoaded && !HasRamadanUrl;

    /// <summary>Creates the ViewModel with its required dependencies.</summary>
    public RamadanViewModel(IMasjidContext context)
    {
        _context = context;
        Title = "Ramadan";
    }

    /// <summary>
    /// Loads the Ramadan URL from the active configuration.
    /// Called when the Ramadan category first gains focus.
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

            RamadanUrl = config?.RamadanUrl;
        }
        finally
        {
            IsBusy = false;
            HasLoaded = true;
        }
    }

    /// <summary>
    /// Opens the Ramadan URL in the system browser. The URL is validated to be
    /// HTTPS before opening.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasRamadanUrl))]
    private async Task OpenRamadanAsync()
    {
        if (DonationNavigationPolicy.GetSafeDonationUri(RamadanUrl) is not { } uri)
        {
            return;
        }

        await Browser.Default
            .OpenAsync(uri, BrowserLaunchMode.SystemPreferred);
    }
}
