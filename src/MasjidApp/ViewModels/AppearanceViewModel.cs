using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasjidApp.Core.Theming;
using MasjidApp.Services;

namespace MasjidApp.ViewModels;

/// <summary>Theme selection shown by the Settings Appearance drill-down.</summary>
public sealed partial class AppearanceViewModel : ViewModelBase
{
    private readonly IThemeService _themeService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAutomaticSelected))]
    [NotifyPropertyChangedFor(nameof(IsDarkSelected))]
    [NotifyPropertyChangedFor(nameof(IsLightSelected))]
    private ThemePreference _selectedTheme;

    public AppearanceViewModel(IThemeService themeService)
    {
        _themeService = themeService;
        _selectedTheme = themeService.Current;
        Title = "Appearance";
    }

    public bool IsAutomaticSelected => SelectedTheme == ThemePreference.System;

    public bool IsDarkSelected => SelectedTheme == ThemePreference.Dark;

    public bool IsLightSelected => SelectedTheme == ThemePreference.Light;

    [RelayCommand]
    private void SelectTheme(ThemePreference preference)
    {
        _themeService.Apply(preference);
        SelectedTheme = preference;
    }
}
