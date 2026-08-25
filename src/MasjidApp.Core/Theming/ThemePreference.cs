namespace MasjidApp.Core.Theming;

/// <summary>
/// User-selectable application theme, modeled independently of any UI framework so the
/// preference and its transition logic stay unit-testable in the core library (FR-ST-03).
/// </summary>
public enum ThemePreference
{
    /// <summary>Follow the operating-system light/dark setting.</summary>
    System = 0,

    /// <summary>Always use the light theme.</summary>
    Light = 1,

    /// <summary>Always use the dark theme.</summary>
    Dark = 2,
}

/// <summary>Pure helpers for <see cref="ThemePreference"/>.</summary>
public static class ThemePreferenceExtensions
{
    /// <summary>
    /// Returns the next preference in a single-control toggle cycle:
    /// System → Light → Dark → System.
    /// </summary>
    public static ThemePreference Next(this ThemePreference preference) => preference switch
    {
        ThemePreference.System => ThemePreference.Light,
        ThemePreference.Light => ThemePreference.Dark,
        ThemePreference.Dark => ThemePreference.System,
        _ => ThemePreference.System,
    };
}
