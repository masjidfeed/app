using CommunityToolkit.Mvvm.ComponentModel;
using MasjidApp.Services;

namespace MasjidApp.ViewModels;

/// <summary>
/// ViewModel for the full-screen image preview page.
/// Receives the image URL via Shell query parameter injection.
/// </summary>
public sealed partial class ImagePreviewViewModel : ViewModelBase, IQueryAttributable
{
    private readonly INavigationService _navigation;

    /// <summary>The image URL to display full-screen.</summary>
    [ObservableProperty]
    private string? _imageUrl;

    /// <summary>Creates the ViewModel with its required dependency.</summary>
    public ImagePreviewViewModel(INavigationService navigation)
    {
        _navigation = navigation;
        Title = string.Empty;
    }

    /// <inheritdoc/>
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("ImageUrl", out var raw))
        {
            ImageUrl = raw as string;
        }
    }
}
