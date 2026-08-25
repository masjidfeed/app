using MasjidApp.Core.Theming;

namespace MasjidApp.Services;

/// <summary>
/// Applies and persists the user's <see cref="ThemePreference"/> at runtime (FR-ST-03).
/// </summary>
public interface IThemeService
{
    /// <summary>The currently applied preference.</summary>
    ThemePreference Current { get; }

    /// <summary>Loads the persisted preference and applies it to the running app.</summary>
    void Initialize();

    /// <summary>Applies and persists the given <paramref name="preference"/>.</summary>
    void Apply(ThemePreference preference);

    /// <summary>Advances to the next preference in the toggle cycle and returns it.</summary>
    ThemePreference Toggle();
}
