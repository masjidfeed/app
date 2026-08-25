using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasjidApp.Core.Configuration;
using MasjidApp.Core.Http;
using MasjidApp.Core.Services;
using MasjidApp.Services;

namespace MasjidApp.ViewModels;

// FR-CM-02, FR-CM-04: contact screen loaded from the active masjid config.

/// <summary>
/// ViewModel for the Contact &amp; Masjid Info screen.
/// Loads contact details and social links from <see cref="IMasjidContext"/> and
/// provides quick-action commands for map, phone, email, website, and social media.
/// </summary>
public sealed partial class ContactViewModel : ViewModelBase
{
    private readonly IMasjidContext _context;
    private readonly IContactActionService _contactActions;

    /// <summary>Masjid display name shown as the page heading.</summary>
    [ObservableProperty]
    private string _masjidName = string.Empty;

    /// <summary>Street / postal address.</summary>
    [ObservableProperty]
    private string _address = string.Empty;

    /// <summary>Phone number.</summary>
    [ObservableProperty]
    private string _phone = string.Empty;

    /// <summary>Contact e-mail address.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSocialLinks))]
    private string _email = string.Empty;

    /// <summary>Website URL.</summary>
    [ObservableProperty]
    private string _website = string.Empty;

    /// <summary>Optional Facebook page URL.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSocialLinks))]
    private string? _facebook;

    /// <summary>Optional Instagram profile URL.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSocialLinks))]
    private string? _instagram;

    /// <summary><c>true</c> when at least one social link is present.</summary>
    public bool HasSocialLinks =>
        !string.IsNullOrWhiteSpace(Facebook) || !string.IsNullOrWhiteSpace(Instagram);

    /// <summary>
    /// Creates the ViewModel with its required dependencies.
    /// </summary>
    public ContactViewModel(IMasjidContext context, IContactActionService contactActions)
    {
        _context = context;
        _contactActions = contactActions;
        Title = "Contact";
    }

    /// <summary>
    /// Loads contact details from the active masjid configuration.
    /// Called on page appearing and pull-to-refresh.
    /// </summary>
    [RelayCommand]
    private Task LoadAsync(CancellationToken ct) =>
        LoadDataAsync(ApiCachePolicy.PreferCache, ct);

    /// <summary>Reloads contact data while bypassing the 24-hour cache window.</summary>
    [RelayCommand]
    private Task RefreshAsync(CancellationToken ct) =>
        LoadDataAsync(ApiCachePolicy.ForceRefresh, ct);

    private async Task LoadDataAsync(ApiCachePolicy cachePolicy, CancellationToken ct)
    {
        IsBusy = true;
        try
        {
            var result = await _context.GetConfigAsync(cachePolicy, ct);
            var config = result?.ValueOrDefault;
            var configUrl = _context.ActiveEntry?.ConfigUrl;

            MasjidName = config?.Masjid?.Name ?? string.Empty;
            Address = config?.Contact?.Address ?? string.Empty;
            Phone = config?.Contact?.Phone ?? string.Empty;
            Email = config?.Contact?.Email ?? string.Empty;
            Website = EndpointUrlResolver.ResolveAgainstOrigin(config?.Contact?.Website, configUrl) ?? string.Empty;
            Facebook = EndpointUrlResolver.ResolveAgainstOrigin(config?.Social?.Facebook, configUrl);
            Instagram = EndpointUrlResolver.ResolveAgainstOrigin(config?.Social?.Instagram, configUrl);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Opens the address in the platform's maps app.</summary>
    [RelayCommand]
    private Task OpenMapAsync() => _contactActions.OpenMapAsync(Address);

    /// <summary>Initiates a phone call using the device dialer.</summary>
    [RelayCommand]
    private void CallPhone() => _contactActions.CallPhone(Phone);

    /// <summary>Opens the default mail client with the contact address pre-filled.</summary>
    [RelayCommand]
    private Task SendEmailAsync() => _contactActions.SendEmailAsync(Email);

    /// <summary>Opens the masjid website in the system browser.</summary>
    [RelayCommand]
    private Task OpenWebsiteAsync() => _contactActions.OpenBrowserAsync(Website);

    /// <summary>Opens the masjid Facebook page in the system browser.</summary>
    [RelayCommand]
    private Task OpenFacebookAsync() => _contactActions.OpenBrowserAsync(Facebook);

    /// <summary>Opens the masjid Instagram profile in the system browser.</summary>
    [RelayCommand]
    private Task OpenInstagramAsync() => _contactActions.OpenBrowserAsync(Instagram);
}
