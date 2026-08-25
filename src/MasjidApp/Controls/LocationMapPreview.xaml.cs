using System.Globalization;
using System.Windows.Input;
using MasjidApp.Localization;
using MasjidApp.Services;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Maps;
using NativeMap = Microsoft.Maui.Controls.Maps.Map;

namespace MasjidApp.Controls;

/// <summary>
/// Displays a non-interactive native map centered on a masjid and delegates taps to the
/// full installed maps application.
/// </summary>
public partial class LocationMapPreview : ContentView
{
    public static readonly BindableProperty AddressProperty = BindableProperty.Create(
        nameof(Address),
        typeof(string),
        typeof(LocationMapPreview),
        string.Empty,
        propertyChanged: OnMapPropertyChanged);

    public static readonly BindableProperty LocationNameProperty = BindableProperty.Create(
        nameof(LocationName),
        typeof(string),
        typeof(LocationMapPreview),
        string.Empty,
        propertyChanged: OnMapPropertyChanged);

    public static readonly BindableProperty LatitudeProperty = BindableProperty.Create(
        nameof(Latitude),
        typeof(double),
        typeof(LocationMapPreview),
        0d,
        propertyChanged: OnMapPropertyChanged);

    public static readonly BindableProperty LongitudeProperty = BindableProperty.Create(
        nameof(Longitude),
        typeof(double),
        typeof(LocationMapPreview),
        0d,
        propertyChanged: OnMapPropertyChanged);

    public static readonly BindableProperty HasLocationProperty = BindableProperty.Create(
        nameof(HasLocation),
        typeof(bool),
        typeof(LocationMapPreview),
        false,
        propertyChanged: OnMapPropertyChanged);

    public static readonly BindableProperty OpenMapCommandProperty = BindableProperty.Create(
        nameof(OpenMapCommand),
        typeof(ICommand),
        typeof(LocationMapPreview));

    private NativeMap? _map;

    /// <summary>Creates an address preview that leaves all gestures to its parent scroll view.</summary>
    public LocationMapPreview()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    /// <summary>Configured street address shown over the map.</summary>
    public string Address
    {
        get => (string)GetValue(AddressProperty);
        set => SetValue(AddressProperty, value);
    }

    /// <summary>Masjid name used to label the native map pin.</summary>
    public string LocationName
    {
        get => (string)GetValue(LocationNameProperty);
        set => SetValue(LocationNameProperty, value);
    }

    /// <summary>Resolved map-center latitude.</summary>
    public double Latitude
    {
        get => (double)GetValue(LatitudeProperty);
        set => SetValue(LatitudeProperty, value);
    }

    /// <summary>Resolved map-center longitude.</summary>
    public double Longitude
    {
        get => (double)GetValue(LongitudeProperty);
        set => SetValue(LongitudeProperty, value);
    }

    /// <summary>Whether the configured address resolved to valid coordinates.</summary>
    public bool HasLocation
    {
        get => (bool)GetValue(HasLocationProperty);
        set => SetValue(HasLocationProperty, value);
    }

    /// <summary>Command that launches the full installed maps application.</summary>
    public ICommand? OpenMapCommand
    {
        get => (ICommand?)GetValue(OpenMapCommandProperty);
        set => SetValue(OpenMapCommandProperty, value);
    }

    /// <summary>Localized accessible label containing the configured masjid address.</summary>
    public string AccessibilityDescription =>
        string.Format(
            CultureInfo.CurrentCulture,
            LocalizationManager.Instance.GetString("About_Map_SemanticDescription"),
            Address);

    private static bool IsNativePreviewConfigured
    {
        get
        {
#if ANDROID && !GOOGLE_MAPS_CONFIGURED
            return false;
#else
            return true;
#endif
        }
    }

    private static void OnMapPropertyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var preview = (LocationMapPreview)bindable;
        preview.OnPropertyChanged(nameof(AccessibilityDescription));
        preview.RefreshMap();
    }

    private void OnLoaded(object? sender, EventArgs e) => RefreshMap();

    private void RefreshMap()
    {
        if (MapHost is null || FallbackContent is null || MapAddressCard is null)
        {
            return;
        }

        var location = new MapLocation(Latitude, Longitude);
        var showMap = IsNativePreviewConfigured && HasLocation && location.IsValid;

        FallbackContent.IsVisible = !showMap;
        MapAddressCard.IsVisible = showMap;
        if (!showMap)
        {
            MapHost.Children.Clear();
            _map = null;
            return;
        }

        _map ??= CreateMap();
        if (!MapHost.Children.Contains(_map))
        {
            MapHost.Children.Add(_map);
        }

        var center = new Location(location.Latitude, location.Longitude);
        _map.Pins.Clear();
        _map.Pins.Add(new Pin
        {
            Label = string.IsNullOrWhiteSpace(LocationName) ? Address : LocationName,
            Address = Address,
            Location = center,
            Type = PinType.Place,
        });
        _map.MoveToRegion(MapSpan.FromCenterAndRadius(center, Distance.FromKilometers(1)));
    }

    private static NativeMap CreateMap() =>
        new()
        {
            InputTransparent = true,
            IsScrollEnabled = false,
            IsZoomEnabled = false,
            IsTrafficEnabled = false,
            IsShowingUser = false,
            MapType = MapType.Street,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
        };
}
