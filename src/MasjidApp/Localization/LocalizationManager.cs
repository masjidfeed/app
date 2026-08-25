using System.Globalization;
using System.Resources;

// TC-05: v1 ships only the neutral (English) AppResources.resx with no satellite
// assemblies. Declaring English as the neutral language with a main-assembly
// fallback stops ResourceManager from probing for a non-existent satellite
// assembly (a benign but noisy FileNotFoundException) on regional cultures.
[assembly: NeutralResourcesLanguage("en", UltimateResourceFallbackLocation.MainAssembly)]

namespace MasjidApp.Localization;

// TC-05: RTL-aware localization manager; English-only in v1, structured for Arabic in v2.

/// <summary>
/// Provides localized strings from <c>AppResources.resx</c> and supports
/// runtime culture switching so Arabic (and other RTL languages) can be added
/// in v2 without restructuring the resource file hierarchy.
/// </summary>
public sealed class LocalizationManager
{
    private static readonly Lazy<LocalizationManager> _instance =
        new(static () => new LocalizationManager());

    private readonly ResourceManager _resourceManager;

    private LocalizationManager()
    {
        _resourceManager = new ResourceManager(
            "MasjidApp.Resources.Strings.AppResources",
            typeof(LocalizationManager).Assembly);
    }

    /// <summary>Singleton instance.</summary>
    public static LocalizationManager Instance => _instance.Value;

    /// <summary>
    /// Returns the localized string for <paramref name="key"/>, or the key itself
    /// if no translation is found (fail-visible rather than silent empty string).
    /// </summary>
    public string this[string key] => GetString(key);

    /// <summary>
    /// Returns the localized string for <paramref name="key"/> using the current
    /// <see cref="CultureInfo.CurrentUICulture"/>.
    /// </summary>
    public string GetString(string key) =>
        _resourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    /// <summary>
    /// Returns the localized string for <paramref name="key"/> formatted with
    /// <paramref name="args"/> using <see cref="string.Format(string, object[])"/>.
    /// </summary>
    public string GetString(string key, params object[] args)
    {
        var template = GetString(key);
        return args.Length > 0 ? string.Format(CultureInfo.CurrentCulture, template, args) : template;
    }

    /// <summary>
    /// Switches the current UI culture and raises <see cref="CultureChanged"/>.
    /// Registered views should rebind to pick up the new strings.
    /// </summary>
    public void SetCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        CultureInfo.CurrentUICulture = culture;
        CultureChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised when the UI culture is changed at runtime.</summary>
    public event EventHandler? CultureChanged;
}
