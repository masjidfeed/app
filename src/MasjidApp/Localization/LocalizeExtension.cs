namespace MasjidApp.Localization;

// TC-05: XAML markup extension for accessing AppResources strings from XAML
// without code-behind, supporting runtime culture switching.

/// <summary>
/// XAML markup extension that returns a localized string for the given resource key.
/// Usage in XAML:
/// <code>
///   xmlns:l="clr-namespace:MasjidApp.Localization"
///   Text="{l:Localize PrayerTimes_Title}"
/// </code>
/// </summary>
[ContentProperty(nameof(Key))]
public sealed class LocalizeExtension : IMarkupExtension<string>
{
    /// <summary>The resource key to look up in <c>AppResources.resx</c>.</summary>
    public string Key { get; set; } = string.Empty;

    /// <inheritdoc/>
    public string ProvideValue(IServiceProvider serviceProvider) =>
        LocalizationManager.Instance.GetString(Key);

    /// <inheritdoc/>
    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) =>
        ProvideValue(serviceProvider);
}
