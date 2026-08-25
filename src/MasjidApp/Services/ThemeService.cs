using MasjidApp.Core.Theming;
#if ANDROID
using AndroidX.AppCompat.App;
using AndroidX.Core.View;
#endif

namespace MasjidApp.Services;

/// <inheritdoc />
public sealed class ThemeService : IThemeService
{
    private const string PreferenceKey = "app_theme";

    /// <inheritdoc />
    public ThemePreference Current { get; private set; } = ThemePreference.System;

    /// <inheritdoc />
    public void Initialize()
    {
        var stored = Preferences.Default.Get(PreferenceKey, (int)ThemePreference.System);
        Apply((ThemePreference)stored);
    }

    /// <inheritdoc />
    public void Apply(ThemePreference preference)
    {
        Current = preference;
        Preferences.Default.Set(PreferenceKey, (int)preference);

#if ANDROID
        AppCompatDelegate.DefaultNightMode = preference switch
        {
            ThemePreference.Light => AppCompatDelegate.ModeNightNo,
            ThemePreference.Dark => AppCompatDelegate.ModeNightYes,
            _ => AppCompatDelegate.ModeNightFollowSystem,
        };
#endif

        if (Application.Current is { } app)
        {
            app.UserAppTheme = preference switch
            {
                ThemePreference.Light => AppTheme.Light,
                ThemePreference.Dark => AppTheme.Dark,
                _ => AppTheme.Unspecified,
            };
        }

#if ANDROID
        ApplyAndroidSystemBarAppearance(preference);
#endif
    }

#if ANDROID
    private static void ApplyAndroidSystemBarAppearance(ThemePreference preference)
    {
        var window = Platform.CurrentActivity?.Window;
        var decorView = window?.DecorView;
        if (window is null || decorView is null)
        {
            return;
        }

        var isDark = preference == ThemePreference.Dark ||
            (preference == ThemePreference.System &&
             Application.Current?.RequestedTheme == AppTheme.Dark);
        var insetsController = WindowCompat.GetInsetsController(window, decorView);
        if (insetsController is null)
        {
            return;
        }

        insetsController.AppearanceLightStatusBars = !isDark;
        insetsController.AppearanceLightNavigationBars = !isDark;
    }
#endif

    /// <inheritdoc />
    public ThemePreference Toggle()
    {
        Apply(Current.Next());
        return Current;
    }
}
