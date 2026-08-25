using MasjidApp.ViewModels;

namespace MasjidApp.Views;

// FR-ST-01, FR-ST-02, FR-ST-03, FR-ST-05, NFR-PR-02: settings & personalization screen.

/// <summary>
/// Settings page - theme, time format, notification preferences, active masjid, and about info.
/// </summary>
public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _viewModel;
    private bool _isVisible;

    /// <summary>
    /// Creates the page with its injected ViewModel.
    /// </summary>
    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.RefreshTheme();
        _viewModel.RefreshPrayerReminderSettings();
        await _viewModel.RefreshPushNotificationSettingsAsync();
        _isVisible = true;
    }

    protected override void OnDisappearing()
    {
        _isVisible = false;
        base.OnDisappearing();
    }

    private async void OnPrayerRemindersToggled(object? sender, ToggledEventArgs e)
    {
        if (_isVisible && !_viewModel.IsUpdatingPrayerReminders)
        {
            await _viewModel.ApplyPrayerReminderSettingsAsync();
        }
    }

    private async void OnPushNotificationsToggled(object? sender, ToggledEventArgs e)
    {
        if (_isVisible && !_viewModel.IsUpdatingPushNotifications)
        {
            await _viewModel.SetPushNotificationsEnabledAsync(e.Value);
        }
    }

}
