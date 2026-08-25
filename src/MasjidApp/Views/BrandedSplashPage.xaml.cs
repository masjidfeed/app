using MasjidApp.Core.Configuration;
using MasjidApp.Core.Http;
using MasjidApp.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MasjidApp.Views;

public partial class BrandedSplashPage : ContentPage
{
    private readonly IServiceProvider _services;
    private readonly IMasjidContext _masjidContext;
    private readonly IMasjidApiClient _api;
    private bool _transitionStarted;

    public BrandedSplashPage(
        IServiceProvider services,
        IMasjidContext masjidContext,
        IMasjidApiClient api)
    {
        _services = services;
        _masjidContext = masjidContext;
        _api = api;
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, EventArgs e)
    {
        if (_transitionStarted)
        {
            return;
        }

        _transitionStarted = true;
        _ = LoadSplashImageAsync();
        await Task.Delay(900);
        if (Window is not null)
        {
            Window.Page = _services.GetRequiredService<AppShell>();
        }
    }

    private async Task LoadSplashImageAsync()
    {
        try
        {
            var configResult = await _masjidContext.GetConfigAsync();
            var imageUrl = EndpointUrlResolver.ResolveAgainstOrigin(
                configResult?.ValueOrDefault?.Branding?.SplashLogoUrl,
                _masjidContext.ActiveEntry?.ConfigUrl);
            var masjidId = _masjidContext.ActiveEntry?.Id;

            if (string.IsNullOrWhiteSpace(imageUrl))
            {
                ShowFallbackImage();
                return;
            }

            if (string.IsNullOrWhiteSpace(masjidId))
            {
                return;
            }

            var imageResult = await _api.GetImageAsync(imageUrl, $"splash:{masjidId}");
            var image = imageResult.ValueOrDefault;
            if (image is null || Window?.Page != this)
            {
                return;
            }

            SplashImage.Source = ImageSource.FromStream(
                () => new MemoryStream(image.Bytes, writable: false));
            SplashImage.IsVisible = true;
        }
        catch (OperationCanceledException)
        {
            // The app is closing or its startup work was cancelled.
        }
#pragma warning disable CA1031 // Startup branding must always degrade to the packaged fallback.
        catch (Exception)
        {
            // Configuration or image failures leave the plain white screen in place.
        }
#pragma warning restore CA1031
    }

    private void ShowFallbackImage()
    {
        if (Window?.Page != this)
        {
            return;
        }

        SplashImage.Source = "setup_logo_light.png";
        SplashImage.IsVisible = true;
    }
}
