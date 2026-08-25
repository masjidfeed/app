using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasjidApp.Core.Events;
using MasjidApp.Core.Models;
using MasjidApp.Services;

namespace MasjidApp.ViewModels;

// FR-CM-01, FR-EV-02, FR-EV-03, FR-EV-07, NFR-SC-02

/// <summary>Displays one post and conditionally exposes event-specific behavior.</summary>
public sealed partial class PostDetailViewModel : ViewModelBase, IQueryAttributable
{
    private readonly INavigationService _navigation;
    private readonly IEventCalendarService _calendarService;
    private readonly IPostActionService _postActions;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEvent))]
    [NotifyPropertyChangedFor(nameof(IsAnnouncement))]
    [NotifyPropertyChangedFor(nameof(DisplayEventDateTime))]
    [NotifyPropertyChangedFor(nameof(HasEventDateTime))]
    [NotifyPropertyChangedFor(nameof(ActiveAlert))]
    [NotifyPropertyChangedFor(nameof(HasActiveAlert))]
    private MasjidPost? _post;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayEventDateTime))]
    [NotifyPropertyChangedFor(nameof(HasEventDateTime))]
    [NotifyPropertyChangedFor(nameof(ActiveAlert))]
    [NotifyPropertyChangedFor(nameof(HasActiveAlert))]
    private MasjidEventOccurrence? _occurrence;

    /// <summary>Whether event-only sections should be displayed.</summary>
    public bool IsEvent => Post?.IsEvent == true;

    /// <summary>Whether announcement publication metadata should be displayed.</summary>
    public bool IsAnnouncement => Post is not null && !Post.IsEvent;

    /// <summary>The selected concrete occurrence, falling back to the post's event start.</summary>
    public DateTimeOffset? DisplayEventDateTime => Occurrence?.OccurrenceDateTime ?? Post?.EventDateTime;

    /// <summary>Whether date and calendar actions have a concrete event occurrence.</summary>
    public bool HasEventDateTime => IsEvent && DisplayEventDateTime is not null;

    /// <summary>The event update active for the selected occurrence.</summary>
    public string? ActiveAlert => Occurrence?.ActiveAlert ?? GetActiveAlert(Post);

    /// <summary>Whether an active event update should be displayed.</summary>
    public bool HasActiveAlert => !string.IsNullOrWhiteSpace(ActiveAlert);

    /// <summary>Creates the unified post detail ViewModel.</summary>
    public PostDetailViewModel(
        INavigationService navigation,
        IEventCalendarService calendarService,
        IPostActionService postActions)
    {
        _navigation = navigation;
        _calendarService = calendarService;
        _postActions = postActions;
        Title = "Post";
    }

    /// <inheritdoc />
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue(nameof(Occurrence), out var occurrenceValue) &&
            occurrenceValue is MasjidEventOccurrence occurrence)
        {
            Occurrence = occurrence;
            Post = occurrence.Event;
        }
        else if (query.TryGetValue(nameof(Post), out var postValue) && postValue is MasjidPost post)
        {
            Post = post;
            Occurrence = null;
        }

        Title = Post?.Title ?? "Post";
    }

    [RelayCommand]
    private Task GoBackAsync() => _navigation.GoBackAsync();

    [RelayCommand]
    private Task BrowseAsync() => _postActions.BrowseAsync(Post?.Url);

    [RelayCommand]
    private Task ShareAsync() => _postActions.ShareAsync(Post?.Title, Post?.Url);

    [RelayCommand]
    private Task PreviewImageAsync(string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return Task.CompletedTask;
        }

        return _navigation.GoToAsync(
            Routes.ImagePreview,
            new Dictionary<string, object> { { "ImageUrl", imageUrl } });
    }

    [RelayCommand]
    private async Task AddToCalendarAsync(CancellationToken ct)
    {
        var occurrence = Occurrence ?? CreateOccurrence(Post);
        if (occurrence is null)
        {
            return;
        }

        await _calendarService.PresentAddEventAsync(
            CalendarEventDraftFactory.Create(occurrence),
            ct);
    }

    private static MasjidEventOccurrence? CreateOccurrence(MasjidPost? post) =>
        post is { IsEvent: true, EventDateTime: { } eventDateTime }
            ? new MasjidEventOccurrence(post, eventDateTime, GetActiveAlert(post))
            : null;

    private static string? GetActiveAlert(MasjidPost? post) =>
        post is not null &&
        !string.IsNullOrWhiteSpace(post.Alert) &&
        post.AlertEndDateTime >= DateTimeOffset.UtcNow
            ? post.Alert
            : null;
}
