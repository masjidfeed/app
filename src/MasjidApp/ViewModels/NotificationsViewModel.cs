using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasjidApp.Core.Push;
using MasjidApp.Core.Services;
using MasjidApp.Notifications;
using Microsoft.Extensions.Logging;

namespace MasjidApp.ViewModels;

public sealed partial class NotificationsViewModel : ViewModelBase
{
    private readonly INotificationInboxService _inbox;
    private readonly IDeliveredNotificationService _deliveredNotifications;
    private readonly IMasjidContext _context;
    private readonly HomeShellViewModel _home;
    private readonly ILogger<NotificationsViewModel> _logger;

    [ObservableProperty]
    private bool _hasNotifications;

    public ObservableCollection<NotificationInboxItem> Notifications { get; } = [];

    public NotificationsViewModel(
        INotificationInboxService inbox,
        IDeliveredNotificationService deliveredNotifications,
        IMasjidContext context,
        HomeShellViewModel home,
        ILogger<NotificationsViewModel> logger)
    {
        _inbox = inbox;
        _deliveredNotifications = deliveredNotifications;
        _context = context;
        _home = home;
        _logger = logger;
        Title = "Notifications";
    }

    public async Task OnAppearingAsync(CancellationToken ct = default)
    {
        var masjidId = CurrentMasjidId;
        if (masjidId is null)
        {
            return;
        }

        var delivered = await _deliveredNotifications.GetDeliveredAsync(ct);
        foreach (var notification in delivered)
        {
            _inbox.Upsert(notification);
        }

        _deliveredNotifications.ClearAll();
        Reload(masjidId);
    }

    public async Task OpenAsync(NotificationInboxItem notification)
    {
        LogNotificationTapStarted(_logger, notification.NotificationId, notification.DeepLink);
        var masjidId = CurrentMasjidId;
        if (masjidId is not null && !notification.IsRead)
        {
            _inbox.MarkRead(masjidId, notification.NotificationId);
        }

        if (notification.DeepLink is null ||
            !Uri.TryCreate(notification.DeepLink, UriKind.Absolute, out var uri) ||
            !PostDeepLink.TryParse(uri, out var deepLink) ||
            deepLink is null)
        {
            LogNotificationDeepLinkInvalid(_logger, notification.NotificationId, notification.DeepLink);
            if (masjidId is not null)
            {
                Reload(masjidId);
            }

            return;
        }

        var navigated = await _home.OpenPostFromCurrentStackAsync(deepLink);
        LogNotificationNavigationCompleted(_logger, notification.NotificationId, navigated);
    }

    public void MarkRead(NotificationInboxItem notification)
    {
        var masjidId = CurrentMasjidId;
        if (masjidId is null || notification.IsRead)
        {
            return;
        }

        _inbox.MarkRead(masjidId, notification.NotificationId);
        Reload(masjidId);
    }

    public void Delete(NotificationInboxItem notification)
    {
        var masjidId = CurrentMasjidId;
        if (masjidId is null)
        {
            return;
        }

        _inbox.Delete(masjidId, notification.NotificationId);
        Reload(masjidId);
    }

    [RelayCommand]
    private void ClearAll()
    {
        var masjidId = CurrentMasjidId;
        if (masjidId is null)
        {
            return;
        }

        _inbox.Clear(masjidId);
        _deliveredNotifications.ClearAll();
        Reload(masjidId);
    }

    private string? CurrentMasjidId => _context.ActiveEntry?.Id;

    private void Reload(string masjidId)
    {
        Notifications.Clear();
        foreach (var notification in _inbox.GetNotifications(masjidId))
        {
            Notifications.Add(notification);
        }

        HasNotifications = Notifications.Count > 0;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Notification inbox tap started. NotificationId={NotificationId}, DeepLink={DeepLink}")]
    private static partial void LogNotificationTapStarted(ILogger logger, string notificationId, string? deepLink);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification inbox deep link is invalid. NotificationId={NotificationId}, DeepLink={DeepLink}")]
    private static partial void LogNotificationDeepLinkInvalid(ILogger logger, string notificationId, string? deepLink);

    [LoggerMessage(Level = LogLevel.Information, Message = "Notification inbox navigation completed. NotificationId={NotificationId}, Navigated={Navigated}")]
    private static partial void LogNotificationNavigationCompleted(ILogger logger, string notificationId, bool navigated);
}