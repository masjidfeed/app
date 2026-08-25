using MasjidApp.ViewModels;

namespace MasjidApp.Views;

/// <summary>Full-screen image preview page.</summary>
public partial class ImagePreviewPage : ContentPage
{
    /// <summary>Creates the page with its injected ViewModel.</summary>
    public ImagePreviewPage(ImagePreviewViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
