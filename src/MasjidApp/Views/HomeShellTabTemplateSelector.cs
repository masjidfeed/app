using MasjidApp.ViewModels;

namespace MasjidApp.Views;

/// <summary>
/// Selects the swipe page template for each home-shell category.
/// </summary>
public sealed class HomeShellTabTemplateSelector : DataTemplateSelector
{
    /// <summary>Template for the Announcements home page.</summary>
    public DataTemplate? AnnouncementsTemplate { get; set; }

    /// <summary>Template for the Ramadan home page.</summary>
    public DataTemplate? RamadanTemplate { get; set; }

    /// <summary>Template for the Events home page.</summary>
    public DataTemplate? EventsTemplate { get; set; }

    /// <summary>Template for the About home page.</summary>
    public DataTemplate? AboutTemplate { get; set; }

    /// <inheritdoc/>
    protected override DataTemplate? OnSelectTemplate(object item, BindableObject container)
    {
        return item switch
        {
            HomeShellCategoryPage { Category: HomeShellCategory.Announcements } => AnnouncementsTemplate,
            HomeShellCategoryPage { Category: HomeShellCategory.Ramadan } => RamadanTemplate,
            HomeShellCategoryPage { Category: HomeShellCategory.Events } => EventsTemplate,
            HomeShellCategoryPage { Category: HomeShellCategory.About } => AboutTemplate,
            _ => AnnouncementsTemplate,
        };
    }
}
