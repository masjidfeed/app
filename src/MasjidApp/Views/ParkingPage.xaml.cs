using System.ComponentModel;
using MasjidApp.Localization;
using MasjidApp.ViewModels;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Maps;
using NativeMap = Microsoft.Maui.Controls.Maps.Map;

namespace MasjidApp.Views;

// API-06 parking feature flag: parking guide page.

/// <summary>
/// Parking guide page - renders a square rounded map with one colored overlay
/// per active parking zone, a legend matching the overlay colors, an
/// Apple-Maps-style floating control column (expand, map type, locate, north),
/// native zoom controls and gestures, recentering on the device location, and
/// a "Validate my parking spot" action backed by the device's location.
/// Uses the same native map provider as the About tab's location preview.
/// </summary>
public partial class ParkingPage : ContentPage
{
    private const double CompactMapSide = 352;

    private readonly ParkingViewModel _viewModel;
    private NativeMap? _map;
    private Pin? _userPin;
    private bool _headingRetryPending;

    /// <summary>Creates the page with its injected ViewModel.</summary>
    public ParkingPage(ParkingViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.Zones.CollectionChanged += OnZonesChanged;
        Loaded += OnPageLoaded;
    }

    /// <inheritdoc/>
    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (_viewModel.LoadCommand.CanExecute(null))
        {
            _viewModel.LoadCommand.Execute(null);
        }
    }

    /// <inheritdoc/>
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.StopCompassFollow();
    }

    private void OnPageLoaded(object? sender, EventArgs e)
    {
        if (_map is null && _viewModel.IsNativeMapConfigured)
        {
            _map = CreateMap();
            MapHost.Children.Add(_map);
#if IOS || MACCATALYST
            // The page has its own north/compass control; the native MKMapView
            // compass button would appear underneath the control stack whenever
            // the map is rotated.
            if (_map.Handler?.PlatformView is MapKit.MKMapView mapUi)
            {
                mapUi.ShowsCompass = false;
            }
#endif
            if (_viewModel.MapRegion is { } region)
            {
                _map.MoveToRegion(region);
            }
        }

        ApplyMapType();
        ResizeMap();
        RefreshPolygons();
        RefreshUserPin();
        _ = ApplyHeadingWithRetryAsync(_viewModel.FollowsCompass ? _viewModel.CompassHeading : 0);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ParkingViewModel.MapRegion) when _map is not null && _viewModel.MapRegion is { } region:
                _map.MoveToRegion(region);
                break;
            case nameof(ParkingViewModel.IsSatellite):
                ApplyMapType();
                break;
            case nameof(ParkingViewModel.IsMapExpanded):
                ResizeMap();
                break;
            case nameof(ParkingViewModel.UserLocation):
                RefreshUserPin();
                break;
            case nameof(ParkingViewModel.CompassHeading) when _viewModel.FollowsCompass:
                // Compass readings arrive off the UI thread; map calls need the main thread.
                _ = ApplyHeadingWithRetryAsync(_viewModel.CompassHeading);
                break;
            case nameof(ParkingViewModel.FollowsCompass):
                // Toggled back to north-up: snap the map to north.
                if (!_viewModel.FollowsCompass)
                {
                    _ = ApplyHeadingWithRetryAsync(0);
                }
                break;
        }
    }

    private void ApplyMapType()
    {
        if (_map is null)
        {
            return;
        }

        // Satellite is the default layer; the regular option is the street map.
        _map.MapType = _viewModel.IsSatellite ? MapType.Satellite : MapType.Street;
    }

    private void ResizeMap()
    {
        if (MapRoot is null)
        {
            return;
        }

        if (_viewModel.IsMapExpanded)
        {
            // Fullscreen: the square card bleeds past the page padding and fills
            // the whole screen; the floating controls give way to a single
            // collapse button.
            MapRoot.Margin = new Thickness(-16, -16, -16, 0);
            MapRoot.WidthRequest = Math.Max(Window?.Width ?? CompactMapSide, CompactMapSide);
            MapRoot.HeightRequest = Math.Max(
                (Window?.Height ?? CompactMapSide) - 96,
                CompactMapSide);
            MapControls.IsVisible = false;
            CollapseButton.IsVisible = true;
        }
        else
        {
            MapRoot.Margin = Thickness.Zero;
            MapRoot.WidthRequest = CompactMapSide;
            MapRoot.HeightRequest = CompactMapSide;
            MapControls.IsVisible = true;
            CollapseButton.IsVisible = false;
        }
    }

    private void RefreshUserPin()
    {
        if (_map is null)
        {
            return;
        }

        if (_viewModel.UserLocation is not { } location)
        {
            return;
        }

        _map.MoveToRegion(MapSpan.FromCenterAndRadius(location, Distance.FromMeters(300)));
        if (_userPin is null)
        {
            _userPin = new Pin
            {
                Label = LocalizationManager.Instance["Parking_UserPin"],
                Location = location,
                Type = PinType.Generic,
            };
            _map.Pins.Add(_userPin);
        }
        else
        {
            _userPin.Location = location;
        }
    }

    // ── Floating map controls ───────────────────────────────────────────────────

    private void OnExpandTapped(object? sender, EventArgs e) =>
        _viewModel.IsMapExpanded = !_viewModel.IsMapExpanded;

    private void OnCollapseTapped(object? sender, EventArgs e) =>
        _viewModel.IsMapExpanded = false;

    private void OnLayersTapped(object? sender, EventArgs e) =>
        _viewModel.IsSatellite = !_viewModel.IsSatellite;

    private void OnLocateTapped(object? sender, EventArgs e)
    {
        if (_viewModel.CenterOnLocationCommand.CanExecute(null))
        {
            _viewModel.CenterOnLocationCommand.Execute(null);
        }
    }

    private void OnNorthTapped(object? sender, EventArgs e)
    {
        if (_viewModel.ToggleCompassCommand.CanExecute(null))
        {
            _viewModel.ToggleCompassCommand.Execute(null);
        }
    }

    /// <summary>
    /// Rotates the map so its top points at <paramref name="headingDegrees"/>
    /// (0 = north). In follow-compass mode the top of the map tracks the phone's
    /// facing direction; heading 0 is the north-up state. Returns <c>false</c>
    /// when the platform map is not ready yet (no camera placed), so callers
    /// can retry until it is.
    /// </summary>
    private bool ApplyHeading(double headingDegrees)
    {
#if ANDROID
        if (_map?.Handler is not Microsoft.Maui.Maps.Handlers.MapHandler handler ||
            handler.Map is not { } googleMap)
        {
            return false;
        }

        // Bearing keeps the current target, zoom, and tilt, so only the rotation changes.
        var current = googleMap.CameraPosition;
        googleMap.MoveCamera(Android.Gms.Maps.CameraUpdateFactory.NewCameraPosition(
            new Android.Gms.Maps.Model.CameraPosition.Builder()
                .Target(current.Target)
                .Zoom(current.Zoom)
                .Tilt(current.Tilt)
                .Bearing((float)headingDegrees)
                .Build()));
        return true;
#elif IOS || MACCATALYST
        if (_map?.Handler?.PlatformView is not MapKit.MKMapView mapUi)
        {
            return false;
        }

        var distance = mapUi.Camera?.CenterCoordinateDistance ?? 0;
        if (distance <= 0)
        {
            // The map has not placed its region yet.
            return false;
        }

        // Heading keeps the current center and zoom distance, so only the
        // rotation changes. Live compass updates use an unanimated camera
        // change to avoid lag jitter.
        var camera = new MapKit.MKMapCamera
        {
            CenterCoordinate = mapUi.CenterCoordinate,
            CenterCoordinateDistance = distance,
            Pitch = mapUi.Camera?.Pitch ?? 0,
            Heading = headingDegrees,
        };
        mapUi.SetCamera(camera, animated: false);
        return true;
#else
        // Other platforms have no rotation gesture, so nothing to apply.
        return true;
#endif
    }

    /// <summary>
    /// Applies the heading on the UI thread, retrying briefly while the native
    /// map is still initializing so the first heading change is never lost.
    /// </summary>
    private async Task ApplyHeadingWithRetryAsync(double headingDegrees)
    {
        if (await ApplyHeadingOnMainThreadAsync(headingDegrees))
        {
            _headingRetryPending = false;
            return;
        }

        if (_headingRetryPending)
        {
            return;
        }

        _headingRetryPending = true;
        for (var attempt = 0; attempt < 20 && _headingRetryPending; attempt++)
        {
            await Task.Delay(250);
            if (await ApplyHeadingOnMainThreadAsync(headingDegrees))
            {
                _headingRetryPending = false;
                return;
            }
        }

        _headingRetryPending = false;
    }

    private Task<bool> ApplyHeadingOnMainThreadAsync(double headingDegrees)
    {
        if (MainThread.IsMainThread)
        {
            return Task.FromResult(ApplyHeading(headingDegrees));
        }

        return MainThread.InvokeOnMainThreadAsync(() => ApplyHeading(headingDegrees));
    }

    private void OnZonesChanged(object? sender, EventArgs e) => RefreshPolygons();

    private void RefreshPolygons()
    {
        if (_map is null)
        {
            return;
        }

        _map.MapElements.Clear();
        foreach (var zone in _viewModel.Zones)
        {
            if (zone.Coordinates.Count < 3)
            {
                continue;
            }

            var color = Color.Parse(zone.ColorHex);
            var polygon = new Polygon
            {
                StrokeColor = color,
                StrokeWidth = 2,
                FillColor = color.WithAlpha(0.35f),
            };

            // The document's outline is not closed; the app closes the shape itself.
            foreach (var location in zone.Coordinates)
            {
                polygon.Geopath.Add(location);
            }

            polygon.Geopath.Add(zone.Coordinates[0]);
            _map.MapElements.Add(polygon);
        }
    }

    private static NativeMap CreateMap() =>
        new()
        {
            IsScrollEnabled = true,
            IsZoomEnabled = true,
            IsTrafficEnabled = false,
            IsShowingUser = false,
            // Satellite is the default layer.
            MapType = MapType.Satellite,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
        };
}
