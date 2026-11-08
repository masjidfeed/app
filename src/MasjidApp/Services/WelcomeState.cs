using Microsoft.Maui.Storage;

namespace MasjidApp.Services;

// Welcome/onboarding: a single persisted flag marks that the welcome showcase was
// shown once, so it never appears again on subsequent launches.

/// <summary>
/// Persists whether the welcome page has been shown.  The flag is written as soon as
/// the user finishes or skips the welcome flow, and read at launch to decide whether
/// the welcome page should be the window root before masjid selection.
/// </summary>
public static class WelcomeState
{
    internal const string ShownPreferenceKey = "masjidapp_welcome_shown";

    /// <summary>Gets whether the welcome page was already shown on a previous launch.</summary>
    public static bool WasShown => Preferences.Default.Get(ShownPreferenceKey, false);

    /// <summary>Persists that the welcome page has been shown (never show it again).</summary>
    public static void MarkShown() => Preferences.Default.Set(ShownPreferenceKey, true);
}
