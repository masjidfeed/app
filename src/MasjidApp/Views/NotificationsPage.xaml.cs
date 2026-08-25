using MasjidApp.ViewModels;

namespace MasjidApp.Views;

public partial class NotificationsPage : ContentPage
{
    private readonly NotificationsViewModel _viewModel;
    private bool _isSwipeSelectionSuppressed;

    public NotificationsPage(NotificationsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.OnAppearingAsync();
    }

    private async void OnNotificationTapped(object? sender, TappedEventArgs e)
    {
        if (_isSwipeSelectionSuppressed)
        {
            return;
        }

        var notification = e.Parameter as Notifications.NotificationInboxItem ??
            (sender as TapGestureRecognizer)?.BindingContext as Notifications.NotificationInboxItem;
        if (notification is null)
        {
            return;
        }

        await _viewModel.OpenAsync(notification);
    }

    private void OnNotificationSwipeStarted(object? sender, SwipeStartedEventArgs e) =>
        _isSwipeSelectionSuppressed = true;

    private void OnNotificationSwipeEnded(object? sender, SwipeEndedEventArgs e)
    {
        Dispatcher.DispatchDelayed(
            TimeSpan.FromMilliseconds(250),
            () => _isSwipeSelectionSuppressed = false);
    }

    private void OnDeleteInvoked(object? sender, EventArgs e)
    {
        if (sender is SwipeItemView { CommandParameter: Notifications.NotificationInboxItem notification })
        {
            _viewModel.Delete(notification);
        }
    }

}