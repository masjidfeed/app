using MasjidApp.ViewModels;

namespace MasjidApp.Views;

// FR-PT-01, FR-PT-02, FR-PT-03, FR-PT-05, FR-PT-09

/// <summary>
/// Prayer times page - displays today's athan/iqama times, a live countdown to the
/// next prayer, an optional Jumuah section, and a 30-day forward schedule.
/// </summary>
public partial class PrayerTimesPage : ContentPage
{
    private readonly PrayerTimesViewModel _viewModel;

    /// <summary>
    /// Creates the page with its injected ViewModel.
    /// </summary>
    public PrayerTimesPage(PrayerTimesViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    /// <inheritdoc/>
    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Start the per-second countdown ticker (FR-PT-02).
        var timer = Application.Current?.Dispatcher.CreateTimer()
            ?? throw new InvalidOperationException("Dispatcher not available.");
        _viewModel.StartCountdownTimer(timer);

        // Reload data each time the page appears.
        if (_viewModel.LoadCommand.CanExecute(null))
        {
            _viewModel.LoadCommand.Execute(null);
        }
    }

    /// <inheritdoc/>
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.StopCountdownTimer();
    }
}
