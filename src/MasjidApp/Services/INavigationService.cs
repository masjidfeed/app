namespace MasjidApp.Services;

/// <summary>
/// Typed wrapper over Shell navigation so ViewModels can navigate without depending on
/// MAUI navigation APIs directly. Registered in the DI container (see <c>MauiProgram</c>).
/// </summary>
public interface INavigationService
{
    /// <summary>Navigates to the given Shell <paramref name="route"/> (see <see cref="Routes"/>).</summary>
    Task GoToAsync(string route, IDictionary<string, object>? parameters = null);

    /// <summary>Navigates back one level in the navigation stack.</summary>
    Task GoBackAsync();
}
