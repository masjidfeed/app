using MasjidApp.ViewModels;

namespace MasjidApp.Views;

// API-06, TC-03, FR-PT-02, NFR-AC-01

/// <summary>
/// Home shell page - hosts the collapsing masjid logo header, the horizontal
/// category strip ("Today", "Events", "Qibla"), and the swappable feed
/// content below.  Code-behind stays thin; all logic lives in
/// <see cref="HomeShellViewModel"/>.
/// </summary>
public partial class HomeShellPage : ContentPage, IAppLifecycleAware
{
    private readonly HomeShellViewModel _viewModel;

    /// <summary>Creates the page with its DI-resolved ViewModel.</summary>
    public HomeShellPage(HomeShellViewModel viewModel)
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

        // Reload data each time the page appears (covers masjid-switch and resume).
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

    void IAppLifecycleAware.OnAppStopped()
    {
        _viewModel.StopCountdownTimer();
        _viewModel.LoadCommand.Cancel();
        _viewModel.RefreshCommand.Cancel();
    }

    void IAppLifecycleAware.OnAppResumed()
    {
        // IWindow.Resumed does not raise Page.Appearing, so explicitly refresh the
        // visible Home page after suspension (FR-CM-01, FR-EV-01, FR-PT-02).
        if (_viewModel.LoadCommand.CanExecute(null))
        {
            _viewModel.LoadCommand.Execute(null);
        }
    }
}
