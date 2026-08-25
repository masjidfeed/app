namespace MasjidApp.Services;

/// <summary>Opens contact destinations through available platform handlers.</summary>
public interface IContactActionService
{
    Task OpenMapAsync(string? address, MapLocation? knownLocation = null);

    void CallPhone(string? phone);

    Task SendEmailAsync(string? email);

    Task OpenBrowserAsync(string? url);
}
