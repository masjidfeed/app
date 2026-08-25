using MasjidApp.Core.Utils;
using Microsoft.Maui.ApplicationModel.DataTransfer;

namespace MasjidApp.Services;

/// <inheritdoc/>
public sealed class PostActionService : IPostActionService
{
    /// <inheritdoc/>
    public async Task BrowseAsync(string? url)
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

    /// <inheritdoc/>
    public async Task ShareAsync(string? title, string? url)
    {
        var uri = ExternalLinkPolicy.GetSafeUri(url);
        if (uri is null)
        {
            return;
        }

        try
        {
            await Share.Default.RequestAsync(new ShareTextRequest
            {
                Title = title,
                Uri = uri.AbsoluteUri,
            });
        }
        catch (Exception)
        {
        }
    }
}