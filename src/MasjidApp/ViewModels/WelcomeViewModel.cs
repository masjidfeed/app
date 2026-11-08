using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasjidApp.Localization;
using MasjidApp.Services;

namespace MasjidApp.ViewModels;

// Welcome/onboarding carousel: showcases the app's features on first launch,
// then hands off to masjid selection (SetupPage) or the shell.

/// <summary>One carousel slide: title, description, and a screenshot image.</summary>
public sealed class WelcomeSlide
{
    /// <summary>Creates a slide.</summary>
    /// <param name="Title">Localized slide heading.</param>
    /// <param name="Description">Localized slide copy.</param>
    /// <param name="Image">Image resource name of the screenshot.</param>
    public WelcomeSlide(string Title, string Description, string Image)
    {
        this.Title = Title;
        this.Description = Description;
        this.Image = Image;
    }

    /// <summary>Localized slide heading.</summary>
    public string Title { get; }

    /// <summary>Localized slide copy.</summary>
    public string Description { get; }

    /// <summary>Image resource name of the screenshot.</summary>
    public string Image { get; }
}

/// <summary>
/// ViewModel for the one-time welcome page.  Hosts the feature showcase slides
/// (announcements, events, prayer times, widgets, notifications, donations, masjid
/// selection), standard dot progress indicators via the bound <see cref="Position"/>,
/// and Next/Skip commands.  Completing the flow (Next on the last slide or Skip)
/// marks the welcome as shown and raises <see cref="Completed"/> so the page can
/// hand off to masjid selection.
/// </summary>
public sealed partial class WelcomeViewModel : ViewModelBase
{
    private static string Localize(string key) =>
        LocalizationManager.Instance.GetString(key);

    private static WelcomeSlide CreateSlide(string titleKey, string descriptionKey, string image) =>
        new(Localize(titleKey), Localize(descriptionKey), image);

    /// <summary>Feature showcase slides, in display order.</summary>
    public ObservableCollection<WelcomeSlide> Slides { get; } = new()
    {
        CreateSlide(
            "Welcome_Slide_Announcements_Title",
            "Welcome_Slide_Announcements_Description",
            "welcome_announcements.png"),
        CreateSlide(
            "Welcome_Slide_Events_Title",
            "Welcome_Slide_Events_Description",
            "welcome_events.png"),
        CreateSlide(
            "Welcome_Slide_PrayerTimes_Title",
            "Welcome_Slide_PrayerTimes_Description",
            "welcome_prayer_times.png"),
        CreateSlide(
            "Welcome_Slide_Widgets_Title",
            "Welcome_Slide_Widgets_Description",
            "welcome_widgets.png"),
        CreateSlide(
            "Welcome_Slide_Notifications_Title",
            "Welcome_Slide_Notifications_Description",
            "welcome_notifications.png"),
        CreateSlide(
            "Welcome_Slide_Donations_Title",
            "Welcome_Slide_Donations_Description",
            "welcome_donations.png"),
        CreateSlide(
            "Welcome_Slide_SelectMasjid_Title",
            "Welcome_Slide_SelectMasjid_Description",
            "welcome_masjid.png"),
    };

    [ObservableProperty]
    private int _position;

    [ObservableProperty]
    private bool _isLastSlide;

    [ObservableProperty]
    private string _nextButtonText = Localize("Welcome_Next_Button");

    /// <summary>Raised once when the user finishes (Next on the last slide) or skips.</summary>
    public event EventHandler? Completed;

    /// <summary>Creates the welcome view model and initializes the slide state.</summary>
    public WelcomeViewModel() => IsLastSlide = Slides.Count == 1;

    partial void OnPositionChanged(int value)
    {
        IsLastSlide = value >= Slides.Count - 1;
        NextButtonText = IsLastSlide
            ? Localize("Welcome_GetStarted_Button")
            : Localize("Welcome_Next_Button");
    }

    [RelayCommand]
    private void Next()
    {
        if (IsLastSlide)
        {
            Complete();
            return;
        }

        Position++;
    }

    [RelayCommand]
    private void Skip() => Complete();

    private void Complete()
    {
        WelcomeState.MarkShown();
        Completed?.Invoke(this, EventArgs.Empty);
    }
}
