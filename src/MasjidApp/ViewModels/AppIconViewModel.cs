using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasjidApp.Core.Configuration;
using MasjidApp.Core.Services;
using MasjidApp.Services;

namespace MasjidApp.ViewModels;

/// <summary>One selectable launcher-icon tile shown by the app-icon drill-down.</summary>
public sealed partial class AppIconOption : ObservableObject
{
    public AppIconOption(
        AppIconPreference preference,
        string label,
        ImageSource icon,
        bool isSelected)
    {
        Preference = preference;
        Label = label;
        Icon = icon;
        _isSelected = isSelected;
    }

    public AppIconPreference Preference { get; }

    /// <summary>Tile caption: the masjid's name or the MasjidFeed icon variant.</summary>
    public string Label { get; }

    /// <summary>Preview artwork shown inside the rounded-rectangle tile.</summary>
    public ImageSource Icon { get; }

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>Launcher-icon selection shown by the Settings app-icon drill-down.</summary>
public sealed partial class AppIconViewModel : ViewModelBase
{
    private readonly IAppIconService _appIconService;
    private readonly TenantBrandingDefinition? _branding;

    public AppIconViewModel(IAppIconService appIconService, IMasjidContext masjidContext)
    {
        _appIconService = appIconService;
        _branding = TenantBranding.Get(masjidContext.ActiveEntry);
        Title = "App icon";

        var selection = appIconService.Current;
        if (selection == AppIconPreference.Masjid && _branding is null)
        {
            // The masjid option only exists for tenants with compiled branding; the
            // OS is already showing the default icon, so surface that as selected.
            selection = AppIconPreference.Dark;
        }

        var options = new List<AppIconOption>();
        if (_branding is not null)
        {
            options.Add(new AppIconOption(
                AppIconPreference.Masjid,
                masjidContext.ActiveEntry?.Name ?? "Masjid",
                ImageSource.FromFile($"{_branding.Id}_appicon.png"),
                isSelected: selection == AppIconPreference.Masjid));
        }

        options.Add(new AppIconOption(
            AppIconPreference.Dark,
            "MasjidFeed Dark",
            ImageSource.FromFile("setup_logo_dark.png"),
            isSelected: selection == AppIconPreference.Dark));
        options.Add(new AppIconOption(
            AppIconPreference.Light,
            "MasjidFeed Light",
            ImageSource.FromFile("setup_logo_light.png"),
            isSelected: selection == AppIconPreference.Light));
        Options = options;
    }

    /// <summary>The icon tiles offered for the active masjid.</summary>
    public IReadOnlyList<AppIconOption> Options { get; }

    [RelayCommand]
    private async Task SelectAsync(AppIconOption? option)
    {
        if (option is null || option.IsSelected)
        {
            return;
        }

        await _appIconService.ApplyAsync(option.Preference, _branding);
        foreach (var candidate in Options)
        {
            candidate.IsSelected = candidate.Preference == option.Preference;
        }
    }
}
