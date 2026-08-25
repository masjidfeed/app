using MasjidApp.ViewModels;

namespace MasjidApp.Views;

public partial class AppearancePage : ContentPage
{
    public AppearancePage(AppearanceViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
