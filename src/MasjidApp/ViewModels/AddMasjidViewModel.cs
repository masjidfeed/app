using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasjidApp.Core.Configuration;
using MasjidApp.Core.Http;
using MasjidApp.Core.Models;
using MasjidApp.Core.Serialization;
using MasjidApp.Core.Services;
using MasjidApp.Services;

namespace MasjidApp.ViewModels;

// FR-ST-06, NFR-SC-02: add a masjid by validating its config URL.

/// <summary>
/// ViewModel for the "Add Masjid" screen.
/// The user enters (or scans via QR) a WordPress base URL; this ViewModel resolves
/// the config endpoint, fetches it, validates the URL (HTTPS or local dev), and
/// adds the masjid to the registry on success.
/// </summary>
public sealed partial class AddMasjidViewModel : ViewModelBase
{
    private readonly IMasjidApiClient _api;
    private readonly IMasjidContext _context;
    private readonly IPushNotificationSettingsService _pushNotificationSettings;
    private readonly INavigationService _navigation;

    /// <summary>The URL the user typed (or decoded from QR).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddByUrlCommand))]
    private string _url = string.Empty;

    /// <summary>Human-readable feedback for the user.</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary><c>true</c> while the validation/fetch is in progress.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddByUrlCommand))]
    private bool _isValidating;

    /// <summary>Whether the website is online but does not expose the Masjid configuration endpoint.</summary>
    [ObservableProperty]
    private bool _showUnsupportedWebsite;

    public AddMasjidViewModel(
        IMasjidApiClient api,
        IMasjidContext context,
        IPushNotificationSettingsService pushNotificationSettings,
        INavigationService navigation)
    {
        _api = api;
        _context = context;
        _pushNotificationSettings = pushNotificationSettings;
        _navigation = navigation;
        Title = "Add Masjid";
    }

    private bool CanAddByUrl => !IsValidating && !string.IsNullOrWhiteSpace(Url);

    /// <summary>
    /// Resolves the config endpoint from <see cref="Url"/>, validates it (HTTPS or allowed
    /// local dev URL), fetches the config, and adds the masjid to the registry.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAddByUrl))]
    private async Task AddByUrlAsync(CancellationToken ct)
    {
        IsValidating = true;
        StatusMessage = string.Empty;
        ShowUnsupportedWebsite = false;

        try
        {
            var configUrl = MasjidConfigUrl.Resolve(Url);
            if (configUrl is null)
            {
                StatusMessage = "Please enter a valid URL.";
                return;
            }

            // NFR-SC-02: reject non-HTTPS URLs except for local development hosts.
            if (!MasjidConfigUrl.IsAllowed(configUrl))
            {
                StatusMessage = "Only HTTPS URLs are accepted for production masjid endpoints.";
                return;
            }

            StatusMessage = "Connecting…";
            var result = await _api
                .GetAsync(
                    configUrl,
                    MasjidCoreJsonContext.Default.MasjidConfig,
                    ApiCachePolicy.ForceRefresh,
                    ct);

            if (result is ApiResult<MasjidConfig>.Failed failed)
            {
                if (failed.StatusCode is { } configStatusCode &&
                    configStatusCode != HttpStatusCode.OK &&
                    MasjidConfigUrl.ResolveWebsite(Url) is { } websiteUrl &&
                    await _api.GetStatusCodeAsync(websiteUrl, ct) == HttpStatusCode.OK)
                {
                    StatusMessage = "Unfortunately, your masjid website doesn't yet support the mobile experience.";
                    ShowUnsupportedWebsite = true;
                    return;
                }

                StatusMessage = $"Could not reach the masjid server: {failed.Message}";
                return;
            }

            var config = result.ValueOrDefault;
            if (config?.Masjid is null || string.IsNullOrWhiteSpace(config.Masjid.Id))
            {
                StatusMessage = "The URL did not return a valid Masjid configuration.";
                return;
            }

            var entry = new MasjidStoredEntry(
                config.Masjid.Id,
                config.Masjid.Name ?? config.Masjid.Id,
                configUrl);

            _context.SetActive(entry);
            StatusMessage = $"'{entry.Name}' added successfully.";
            Url = string.Empty;
            await NavigateToSettingsIfNotificationsDisabledAsync(config, ct);
        }
        finally
        {
            IsValidating = false;
        }
    }

    /// <summary>
    /// When the masjid's config enables push notifications but device
    /// notifications are disabled, navigates to Settings where the user is
    /// prompted to enable them.
    /// </summary>
    private async Task NavigateToSettingsIfNotificationsDisabledAsync(
        MasjidConfig config,
        CancellationToken ct)
    {
        if (config.FeatureFlags?.PushNotifications != true ||
            await _pushNotificationSettings.IsDeviceNotificationsEnabledAsync(ct))
        {
            return;
        }

        try
        {
            await _navigation.GoToAsync(Routes.Settings);
        }
        catch (InvalidOperationException)
        {
            // No Shell is available yet (e.g. the mandatory first-run setup page is
            // the window root); the user can enable notifications from Settings later.
        }
    }

}
