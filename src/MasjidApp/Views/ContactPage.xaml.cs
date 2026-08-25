using MasjidApp.ViewModels;

namespace MasjidApp.Views;

// FR-CM-02, FR-CM-04: contact details with quick-action buttons.

/// <summary>
/// Contact &amp; Masjid Info page - displays address, phone, email, website,
/// and optional social media links with tap-to-act buttons.
/// </summary>
public partial class ContactPage : ContentPage
{
    private readonly ContactViewModel _viewModel;

    /// <summary>
    /// Creates the page with its injected ViewModel.
    /// </summary>
    public ContactPage(ContactViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    /// <inheritdoc/>
    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (_viewModel.LoadCommand.CanExecute(null))
        {
            _viewModel.LoadCommand.Execute(null);
        }
    }
}
