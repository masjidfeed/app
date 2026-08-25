using MasjidApp.Core.Utils;
using Microsoft.Extensions.Logging;
using EmailComposer = Microsoft.Maui.ApplicationModel.Communication.Email;
using EmailMessage = Microsoft.Maui.ApplicationModel.Communication.EmailMessage;

namespace MasjidApp.Services;

/// <inheritdoc/>
public sealed partial class ContactActionService : IContactActionService
{
    private readonly IMapLocationResolver _mapLocationResolver;
    private readonly ILogger<ContactActionService> _logger;

    /// <summary>Creates contact actions backed by the shared map-location resolver.</summary>
    public ContactActionService(
        IMapLocationResolver mapLocationResolver,
        ILogger<ContactActionService> logger)
    {
        _mapLocationResolver = mapLocationResolver;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task OpenMapAsync(string? address, MapLocation? knownLocation = null)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return;
        }

        var resolved = knownLocation is { IsValid: true }
            ? knownLocation
            : await _mapLocationResolver.ResolveAsync(address);
        if (resolved is { } resolvedLocation)
        {
            try
            {
                await Map.Default.OpenAsync(
                    new Location(resolvedLocation.Latitude, resolvedLocation.Longitude),
                    new MapLaunchOptions { Name = address });
                return;
            }
            catch (Exception exception)
            {
                LogMapApplicationFailed(exception);
            }
        }

        var encoded = Uri.EscapeDataString(address);
        try
        {
            await Launcher.Default.OpenAsync(
                DeviceInfo.Platform == DevicePlatform.Android
                    ? $"geo:0,0?q={encoded}"
                    : $"https://maps.apple.com/?q={encoded}");
        }
        catch (Exception exception)
        {
            LogMapUriFailed(exception);
            await OpenBrowserAsync($"https://maps.google.com/?q={encoded}");
        }
    }

    /// <inheritdoc/>
    public void CallPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return;
        }

        try
        {
            PhoneDialer.Default.Open(phone);
        }
        catch (Exception)
        {
        }
    }

    /// <inheritdoc/>
    public async Task SendEmailAsync(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return;
        }

        try
        {
            await EmailComposer.Default.ComposeAsync(new EmailMessage { To = [email] });
        }
        catch (Exception)
        {
        }
    }

    /// <inheritdoc/>
    public async Task OpenBrowserAsync(string? url)
    {
        var uri = ExternalLinkPolicy.GetSafeUri(url);
        if (uri is null)
        {
            return;
        }

        try
        {
            await Browser.Default.OpenAsync(uri, BrowserLaunchMode.SystemPreferred);
        }
        catch (Exception)
        {
        }
    }

    [LoggerMessage(
        EventId = 2201,
        Level = LogLevel.Warning,
        Message = "Unable to open the installed maps application.")]
    private partial void LogMapApplicationFailed(Exception exception);

    [LoggerMessage(
        EventId = 2202,
        Level = LogLevel.Warning,
        Message = "Unable to open the platform map URI; using the HTTPS fallback.")]
    private partial void LogMapUriFailed(Exception exception);
}
