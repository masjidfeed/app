namespace MasjidApp.Services;

/// <summary>Shows modal alerts from ViewModels without depending on a page instance.</summary>
public interface IAlertService
{
    /// <summary>
    /// Shows a two-button alert. Returns <c>true</c> when the accept button is tapped.
    /// </summary>
    Task<bool> DisplayAlertAsync(string title, string message, string accept, string cancel);
}
