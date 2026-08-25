using MasjidApp.ViewModels;

namespace MasjidApp.Views;

/// <summary>Masjid selection screen - shows added masjids and lets the user switch.</summary>
public partial class MasjidSelectionPage : ContentPage
{
    public MasjidSelectionPage(MasjidSelectionViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is MasjidSelectionViewModel vm)
        {
            vm.Refresh();
        }
    }
}
