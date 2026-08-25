namespace MasjidApp.Services;

/// <summary>Opens and shares event or announcement post links through platform handlers.</summary>
public interface IPostActionService
{
    Task BrowseAsync(string? url);

    Task ShareAsync(string? title, string? url);
}