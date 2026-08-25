using MasjidApp.ViewModels;

namespace MasjidApp.Views;

// FR-CM-03, NFR-PR-01, NFR-AC-01: Qibla compass page.

/// <summary>
/// Qibla compass page - shows a compass needle pointing toward the Kaaba in Mecca,
/// computed from the device's current location and live magnetometer heading.
/// Degrades gracefully when location is denied or the sensor is unavailable.
/// </summary>
public partial class QiblaPage : ContentPage
{
    private readonly QiblaViewModel _viewModel;

    /// <summary>
    /// Creates the page with its injected ViewModel.
    /// </summary>
    public QiblaPage(QiblaViewModel viewModel)
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

    /// <inheritdoc/>
    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        // Release the compass sensor when the page is hidden to save battery (NFR-PR-01).
        _viewModel.StopCompass();
    }
}
