using MasjidApp.Core.Configuration;
using MasjidApp.Core.Services;
using MasjidApp.Services;
using MasjidApp.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace MasjidApp.Views;

// Welcome/onboarding: one-time feature showcase shown as the window root before
// masjid selection (or before the shell when a masjid is already active, e.g. after
// upgrading an installed app). The shown-once flag lives in WelcomeState.

/// <summary>
/// Full-screen welcome page shown once at first launch.  Displays the feature
/// carousel from <see cref="WelcomeViewModel"/>; when the user finishes or skips,
/// the window root is swapped to <see cref="SetupPage"/> (no masjid configured) or
/// to the branded splash / shell (masjid already active) - mirroring the first-run
/// flow used by <see cref="SetupPage"/>.
/// </summary>
public partial class WelcomePage : ContentPage
{
    private readonly IServiceProvider _services;
    private readonly IMasjidContext _masjidContext;
    private readonly WelcomeViewModel _viewModel;

    /// <summary>Creates the welcome page with DI-resolved dependencies.</summary>
    public WelcomePage(
        WelcomeViewModel viewModel,
        IServiceProvider services,
        IMasjidContext masjidContext)
    {
        _services = services;
        _masjidContext = masjidContext;
        _viewModel = viewModel;
        InitializeComponent();
        BindingContext = viewModel;
        viewModel.Completed += OnWelcomeCompleted;
    }

    /// <summary>
    /// Blocks the Android hardware back button - the welcome flow is part of the
    /// mandatory first-run sequence; Skip is the explicit way out (F2 consistency).
    /// </summary>
    protected override bool OnBackButtonPressed() => true;

    private void OnWelcomeCompleted(object? sender, EventArgs e)
    {
        // Unsubscribe immediately so a completed welcome can never fire twice.
        _viewModel.Completed -= OnWelcomeCompleted;

        // Swap window root on the UI thread: masjid selection when no masjid is
        // configured, otherwise the same splash transition SetupPage uses.
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (Application.Current?.Windows is not { Count: > 0 } windows)
            {
                return;
            }

            windows[0].Page = _masjidContext.ActiveEntry is null
                ? _services.GetRequiredService<SetupPage>()
                : TenantBranding.Get(_masjidContext.ActiveEntry) is not null
                    ? _services.GetRequiredService<BrandedSplashPage>()
                    : _services.GetRequiredService<AppShell>();
        });
    }
}
