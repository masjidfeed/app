using MasjidApp.ViewModels;

namespace MasjidApp.Views;

/// <summary>Unified announcement and event detail page.</summary>
public partial class PostDetailPage : ContentPage
{
    private bool _openingImagePreview;

    /// <summary>Creates the page with its injected ViewModel.</summary>
    public PostDetailPage(PostDetailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    private async void OnPostImageTapped(object? sender, TappedEventArgs e)
    {
        if (_openingImagePreview || PostImage.Source is not { } source)
        {
            return;
        }

        _openingImagePreview = true;
        try
        {
            await Navigation.PushModalAsync(new ImagePreviewPage(source), animated: false);
        }
        finally
        {
            _openingImagePreview = false;
        }
    }
}
