using MasjidApp.ViewModels;

namespace MasjidApp.Views;

/// <summary>
/// App-icon selection drill-down from Settings. Shows the compiled
/// launcher icons as rounded-rectangle tiles and applies the OS icon on selection.
/// </summary>
public partial class AppIconPage : ContentPage
{
    public AppIconPage(AppIconViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
