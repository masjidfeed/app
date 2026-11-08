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
/// per active parking zone, a legend matching the overlay colors, satellite or
/// regular map layers selected with the platform Picker, native zoom controls
/// and gestures, recentering on the device location, and a "Validate my
/// parking spot" action backed by the device's location.
/// Uses the same native map provider as the About tab's location preview.
/// </summary>
public partial class ParkingPage : ContentPage
{
    private readonly ParkingViewModel _viewModel;
    private NativeMap? _map;
    private Pin? _userPin;

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

    private void OnPageLoaded(object? sender, EventArgs e)
    {
        if (_map is null && _viewModel.IsNativeMapConfigured)
        {
            _map = CreateMap();
            MapHost.Children.Add(_map);
            if (_viewModel.MapRegion is { } region)
            {
                _map.MoveToRegion(region);
            }
        }

        ApplyMapType();
        RefreshPolygons();
        RefreshUserPin();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ParkingViewModel.MapRegion) when _map is not null && _viewModel.MapRegion is { } region:
                _map.MoveToRegion(region);
                break;
            case nameof(ParkingViewModel.SelectedMapTypeIndex):
                ApplyMapType();
                break;
            case nameof(ParkingViewModel.UserLocation):
                RefreshUserPin();
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

    // ── Recenter ────────────────────────────────────────────────────────────────

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
