using Microsoft.Maui.Storage;

namespace MasjidApp.Services;

/// <summary>
/// Production implementation of <see cref="IUserPreferences"/> backed by
/// <see cref="Microsoft.Maui.Storage.Preferences.Default"/>.
/// </summary>
public sealed class UserPreferences : IUserPreferences
{
    /// <inheritdoc />
    public T GetValue<T>(string key, T defaultValue) =>
        Preferences.Default.Get(key, defaultValue);

    /// <inheritdoc />
    public void SetValue<T>(string key, T value) =>
        Preferences.Default.Set(key, value);
}
