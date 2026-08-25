using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasjidApp.Core.Configuration;
using MasjidApp.Core.Services;
using MasjidApp.Services;

namespace MasjidApp.ViewModels;

// FR-ST-05, FR-CM-02, FR-CM-04, API-06, API-10: About screen loaded from the active
// masjid config, surfaced as a category in the Home shell category strip (Batch B8).

/// <summary>
/// ViewModel for the About category in the Home shell.
/// Loads masjid identity, contact details, and social links from
/// <see cref="IMasjidContext"/> and provides quick-action commands for map, phone,
/// email, website, and social media (FR-CM-02, FR-CM-04).
/// </summary>
public sealed partial class AboutViewModel : ViewModelBase
{
    private readonly IMasjidContext _context;
    private readonly IContactActionService _contactActions;
    private readonly IMapLocationResolver _mapLocationResolver;

    /// <summary>Masjid display name.</summary>
    [ObservableProperty]
    private string _masjidName = string.Empty;

    /// <summary>Absolute URL of the masjid logo; <c>null</c> when unavailable.</summary>
    [ObservableProperty]
    private string? _logoUrl;

    /// <summary>Street / postal address.</summary>
    [ObservableProperty]
    private string _address = string.Empty;

    /// <summary>Resolved latitude used by the embedded map preview.</summary>
    [ObservableProperty]
    private double _mapLatitude;

    /// <summary>Resolved longitude used by the embedded map preview.</summary>
    [ObservableProperty]
    private double _mapLongitude;

    /// <summary><c>true</c> when the active masjid address resolved to preview coordinates.</summary>
    [ObservableProperty]
    private bool _hasMapLocation;

    /// <summary>Phone number.</summary>
    [ObservableProperty]
    private string _phone = string.Empty;

    /// <summary>Contact e-mail address.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSocialLinks))]
    private string _email = string.Empty;

    /// <summary>Website URL (absolute).</summary>
    [ObservableProperty]
    private string _website = string.Empty;

    /// <summary>Optional Facebook page URL (absolute).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSocialLinks))]
    private string? _facebook;

    /// <summary>Optional Instagram profile URL (absolute).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSocialLinks))]
    private string? _instagram;

    /// <summary>Optional WhatsApp chat/group URL (absolute).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSocialLinks))]
    private string? _whatsApp;

    /// <summary><c>true</c> when at least one social link is present.</summary>
    public bool HasSocialLinks =>
        !string.IsNullOrWhiteSpace(Facebook) || !string.IsNullOrWhiteSpace(Instagram) || !string.IsNullOrWhiteSpace(WhatsApp);

    /// <summary>Creates the ViewModel with its required dependencies.</summary>
    public AboutViewModel(
        IMasjidContext context,
        IContactActionService contactActions,
        IMapLocationResolver mapLocationResolver)
    {
        _context = context;
        _contactActions = contactActions;
        _mapLocationResolver = mapLocationResolver;
        Title = "About";
    }

    /// <summary>
    /// Loads masjid identity, contact details, and social links from the active config.
    /// Called when the About category is selected and on active-masjid switch.
    /// </summary>
    [RelayCommand]
    private async Task LoadAsync(CancellationToken ct)
    {
        IsBusy = true;
        try
        {
            HasMapLocation = false;
            MapLatitude = 0;
            MapLongitude = 0;

            var result = await _context.GetConfigAsync(ct: ct);
            var config = result?.ValueOrDefault;
            var configUrl = _context.ActiveEntry?.ConfigUrl;

            MasjidName = config?.Masjid?.Name ?? string.Empty;
            LogoUrl = EndpointUrlResolver.ResolveAgainstOrigin(config?.Branding?.LogoUrl, configUrl);
            Address = config?.Contact?.Address ?? string.Empty;
            Phone = config?.Contact?.Phone ?? string.Empty;
            Email = config?.Contact?.Email ?? string.Empty;

            // Website and social links may be relative paths in the config document
            // (e.g. "/" or "/facebook") - resolve them against the masjid origin
            // so they open correctly (API-10).
            Website = EndpointUrlResolver.ResolveAgainstOrigin(config?.Contact?.Website, configUrl) ?? string.Empty;
            Facebook = EndpointUrlResolver.ResolveAgainstOrigin(config?.Social?.Facebook, configUrl);
            Instagram = EndpointUrlResolver.ResolveAgainstOrigin(config?.Social?.Instagram, configUrl);
            WhatsApp = EndpointUrlResolver.ResolveAgainstOrigin(config?.Social?.WhatsApp, configUrl);

            var mapLocation = await _mapLocationResolver.ResolveAsync(Address, ct);
            if (mapLocation is { } location)
            {
                MapLatitude = location.Latitude;
                MapLongitude = location.Longitude;
                HasMapLocation = true;
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation when the category is navigated away from.
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Opens the native maps app at the masjid address (FR-CM-02).
    /// Falls back to Google Maps in the browser when the native handler is unavailable.
    /// </summary>
    [RelayCommand]
    private Task OpenMapAsync() =>
        _contactActions.OpenMapAsync(
            Address,
            HasMapLocation ? new MapLocation(MapLatitude, MapLongitude) : null);

    /// <summary>Opens the device dialer pre-filled with the masjid phone number (FR-CM-02).</summary>
    [RelayCommand]
    private void CallPhone() => _contactActions.CallPhone(Phone);

    /// <summary>Opens the email composer pre-addressed to the masjid (FR-CM-02).</summary>
    [RelayCommand]
    private Task SendEmailAsync() => _contactActions.SendEmailAsync(Email);

    /// <summary>Opens the masjid website in the system browser (FR-CM-02).</summary>
    [RelayCommand]
    private Task OpenWebsiteAsync() => _contactActions.OpenBrowserAsync(Website);

    /// <summary>Opens the masjid Facebook page in the system browser (FR-CM-04).</summary>
    [RelayCommand]
    private Task OpenFacebookAsync() => _contactActions.OpenBrowserAsync(Facebook);

    /// <summary>Opens the masjid Instagram profile in the system browser (FR-CM-04).</summary>
    [RelayCommand]
    private Task OpenInstagramAsync() => _contactActions.OpenBrowserAsync(Instagram);

    /// <summary>Opens the masjid WhatsApp chat/group in the system browser (FR-CM-04).</summary>
    [RelayCommand]
    private Task OpenWhatsAppAsync() => _contactActions.OpenBrowserAsync(WhatsApp);

    /// <summary>Contact field key for the phone row.</summary>
    public const string PhoneField = "phone";

    /// <summary>Contact field key for the e-mail row.</summary>
    public const string EmailField = "email";

    /// <summary>Contact field key for the website row.</summary>
    public const string WebsiteField = "website";

    private static readonly TimeSpan CopyConfirmationDuration = TimeSpan.FromSeconds(2);

    private int _copyGeneration;

    /// <summary>
    /// Key of the contact row whose value was most recently copied, or <c>null</c>.
    /// Drives the transient copy-confirmation checkmark.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPhoneCopied))]
    [NotifyPropertyChangedFor(nameof(IsEmailCopied))]
    [NotifyPropertyChangedFor(nameof(IsWebsiteCopied))]
    private string? _copiedField;

    /// <summary><c>true</c> while the phone copy confirmation is showing.</summary>
    public bool IsPhoneCopied => CopiedField == PhoneField;

    /// <summary><c>true</c> while the e-mail copy confirmation is showing.</summary>
    public bool IsEmailCopied => CopiedField == EmailField;

    /// <summary><c>true</c> while the website copy confirmation is showing.</summary>
    public bool IsWebsiteCopied => CopiedField == WebsiteField;

    /// <summary>
    /// Copies the value of the named contact row to the system clipboard and shows a
    /// checkmark on that row for a short confirmation window.
    /// </summary>
    [RelayCommand]
    private async Task CopyAsync(string? field)
    {
        var value = field switch
        {
            PhoneField => Phone,
            EmailField => Email,
            WebsiteField => Website,
            _ => null,
        };

        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        await Clipboard.Default.SetTextAsync(value);

        CopiedField = field;
        var generation = ++_copyGeneration;
        await Task.Delay(CopyConfirmationDuration);

        // Only clear if no later copy superseded this one.
        if (_copyGeneration == generation)
        {
            CopiedField = null;
        }
    }
}
