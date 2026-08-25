namespace MasjidApp.Services;

/// <summary>
/// Testable abstraction over <see cref="Microsoft.Maui.Storage.Preferences"/> for persisting
/// user preference values across launches (FR-ST-03, FR-ST-02).
/// </summary>
public interface IUserPreferences
{
    /// <summary>Gets the value stored under <paramref name="key"/>, or <paramref name="defaultValue"/> if absent.</summary>
    T GetValue<T>(string key, T defaultValue);

    /// <summary>Persists <paramref name="value"/> under <paramref name="key"/>.</summary>
    void SetValue<T>(string key, T value);
}
