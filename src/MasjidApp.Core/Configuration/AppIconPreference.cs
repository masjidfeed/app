namespace MasjidApp.Core.Configuration;

/// <summary>
/// User-selectable launcher icon variants shown by the Settings app-icon drill-down
///. <see cref="Masjid"/> is the default and preserves the historic
/// behaviour of switching to the active tenant's branded icon when one is compiled.
/// </summary>
public enum AppIconPreference
{
    /// <summary>The active masjid's branded icon; falls back to the default icon when no branded assets exist.</summary>
    Masjid = 0,

    /// <summary>The default MasjidFeed dark icon (the app's primary icon).</summary>
    Dark = 1,

    /// <summary>The MasjidFeed light icon.</summary>
    Light = 2,
}
