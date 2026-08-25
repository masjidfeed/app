using MasjidApp.ViewModels;

namespace MasjidApp.Views;

/// <summary>Unified announcement and event detail page.</summary>
public partial class PostDetailPage : ContentPage
{
    /// <summary>Creates the page with its injected ViewModel.</summary>
    public PostDetailPage(PostDetailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
