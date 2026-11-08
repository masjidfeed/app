using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasjidApp.Core.Http;
using MasjidApp.Core.Models;
using MasjidApp.Core.Parking;
using MasjidApp.Core.Services;
using MasjidApp.Localization;
using MasjidApp.PlatformPermissions;
using MasjidApp.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Maps;

namespace MasjidApp.ViewModels;

// API-06 parking feature flag: parking guide page with zone overlays and
// current-location validation (see backend/api.md #parking-map).

/// <summary>Rendered parking zone for the map and the zone list.</summary>
public sealed record ParkingZoneItem(
    string Id,
    string Name,
    string TypeName,
    string ColorHex,
    string? Message,
    IReadOnlyList<Location> Coordinates,
    bool IsAllowed);

/// <summary>Legend row shown underneath the map.</summary>
public sealed record ParkingLegendItem(string ColorHex, string Label);

/// <summary>
/// ViewModel for the parking guide page: loads the parking map document,
/// exposes zone overlays with their colors and the legend, and validates the
/// device's current position against the active parking zones.
/// </summary>
public sealed partial class ParkingViewModel : ViewModelBase
{
    private const string AllowedColor = "#2E7D32";
    private const string NoParkingColor = "#C62828";
    private const string EventRestrictedColor = "#F9A825";

    private readonly IMasjidContext _context;
    private readonly IParkingConfigService _parkingConfigService;
    private readonly ILocationService _locationService;

    private ParkingConfig? _config;
    private bool _hasActiveEvent;

    /// <summary>Whether native maps are configured on this platform.</summary>
    public bool IsNativeMapConfigured { get; } = NativeMapsConfigured();

    private static bool NativeMapsConfigured() =>
#if ANDROID && !GOOGLE_MAPS_CONFIGURED
        false;
#else
        true;
#endif

    /// <summary>Whether the parking document loaded with at least one zone.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoadError))]
    private bool _hasParkingConfig;

    /// <summary>Whether loading completed without a usable parking document.</summary>
    public bool ShowLoadError => !IsBusy && !HasParkingConfig;

    /// <summary>Zone overlays to draw on the map.</summary>
    public System.Collections.ObjectModel.ObservableCollection<ParkingZoneItem> Zones { get; } = [];

    /// <summary>Legend rows shown underneath the map.</summary>
    public System.Collections.ObjectModel.ObservableCollection<ParkingLegendItem> Legend { get; } =
    [
        new(AllowedColor, L("Parking_Legend_Allowed")),
        new(EventRestrictedColor, L("Parking_Legend_EventRestricted")),
        new(NoParkingColor, L("Parking_Legend_NoParking")),
    ];

    /// <summary>Map region covering the masjid center and every zone outline.</summary>
    [ObservableProperty]
    private MapSpan? _mapRegion;

    /// <summary>Shown when an event schedule is currently active (its name).</summary>
    [ObservableProperty]
    private string? _activeEventBanner;

    /// <summary>Localized map-layer options for the Picker; index 0 is satellite (the default).</summary>
    public IReadOnlyList<string> MapTypeOptions =>
    [
        L("Parking_MapType_Satellite"),
        L("Parking_MapType_Street"),
    ];

    /// <summary>Selected map layer: 0 = satellite (default), 1 = regular street map.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSatellite))]
    private int _selectedMapTypeIndex;

    /// <summary><c>true</c> when the map shows satellite imagery (the default); <c>false</c> for the regular street map.</summary>
    public bool IsSatellite => SelectedMapTypeIndex == 0;

    /// <summary>Device location from the most recent "center on my location" action.</summary>
    [ObservableProperty]
    private Location? _userLocation;

    /// <summary>Transient status for map actions (centering failures, etc.).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMapStatus))]
    private string? _mapStatusMessage;

    /// <summary>Whether a map-action status message is currently shown.</summary>
    public bool HasMapStatus => MapStatusMessage is not null;

    /// <summary>Latest validation message, or <c>null</c> when none yet.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidation))]
    private string? _validationMessage;

    /// <summary>Theme color for the validation banner.</summary>
    [ObservableProperty]
    private string _validationColorHex = AllowedColor;

    /// <summary>Whether any validation outcome is currently shown.</summary>
    public bool HasValidation => ValidationMessage is not null;

    /// <summary>Whether the latest validation said the spot is allowed.</summary>
    [ObservableProperty]
    private bool _validationIsAllowed;

    /// <summary>Creates the ViewModel with its required dependencies.</summary>
    public ParkingViewModel(
        IMasjidContext context,
        IParkingConfigService parkingConfigService,
        ILocationService locationService)
    {
        _context = context;
        _parkingConfigService = parkingConfigService;
        _locationService = locationService;
        Title = L("Parking_Title");
    }

    /// <summary>
    /// Loads the masjid config (feature flag + parking endpoint) and the parking
    /// document, then rebuilds the zone overlays. Called on page appearing.
    /// </summary>
    [RelayCommand]
    private async Task LoadAsync(CancellationToken ct)
    {
        IsBusy = true;
        ValidationMessage = null;
        ActiveEventBanner = null;
        try
        {
            var configResult = await _context.GetConfigAsync(ct: ct);
            var config = configResult?.ValueOrDefault;
            var parkingUrl = config?.Endpoints?.ParkingUrl;

            HasParkingConfig = config?.FeatureFlags?.Parking == true &&
                !string.IsNullOrWhiteSpace(parkingUrl);
            if (!HasParkingConfig)
            {
                Zones.Clear();
                MapRegion = null;
                _config = null;
                return;
            }

            var result = await _parkingConfigService.GetParkingConfigAsync(
                parkingUrl!, ApiCachePolicy.PreferCache, ct);
            var parking = result?.ValueOrDefault;
            if (parking?.Zones is not { Count: > 0 })
            {
                HasParkingConfig = false;
                Zones.Clear();
                MapRegion = null;
                _config = null;
                return;
            }

            _config = parking;
            _hasActiveEvent = ParkingMap.GetActiveEvent(parking, DateTimeOffset.Now) is not null;
            ActiveEventBanner = _hasActiveEvent
                ? L("Parking_ActiveEvent")
                : null;

            RebuildZones();
            MapRegion = BuildRegion(parking);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RebuildZones()
    {
        if (_config is not { } parking)
        {
            return;
        }

        Zones.Clear();
        foreach (var zone in ParkingMap.GetActiveZones(parking, DateTimeOffset.Now))
        {
            var kind = ParkingMap.ParseZoneKind(zone.Type);
            if (kind is null)
            {
                // Unknown type - skipped until the app is updated for it (closed value set).
                continue;
            }

            Zones.Add(new ParkingZoneItem(
                zone.Id ?? string.Empty,
                zone.Name ?? zone.Id ?? string.Empty,
                ZoneTypeName(kind.Value),
                ColorFor(kind.Value),
                zone.Message,
                (zone.Coordinates ?? [])
                    .Select(c => new Location(c.Latitude, c.Longitude))
                    .ToList(),
                ParkingMap.IsZoneAllowed(zone, _hasActiveEvent)));
        }
    }

    private static MapSpan? BuildRegion(ParkingConfig parking)
    {
        var center = parking.Center;
        if (center is null)
        {
            return null;
        }

        var allPoints = (parking.Zones ?? [])
            .SelectMany(zone => zone.Coordinates ?? [])
            .Append(center)
            .ToList();
        if (allPoints.Count == 0)
        {
            return null;
        }

        var minLat = allPoints.Min(p => p.Latitude);
        var maxLat = allPoints.Max(p => p.Latitude);
        var minLon = allPoints.Min(p => p.Longitude);
        var maxLon = allPoints.Max(p => p.Longitude);

        var midLat = (minLat + maxLat) / 2;
        var midLon = (minLon + maxLon) / 2;
        var latRadius = Math.Max((maxLat - minLat) / 2, 0.001);
        var lonRadius = Math.Max((maxLon - minLon) / 2, 0.001);
        var distanceKm = Math.Max(
            Math.Abs(latRadius) * 111,
            Math.Abs(lonRadius) * 111 * Math.Cos(midLat * Math.PI / 180));

        return MapSpan.FromCenterAndRadius(
            new Location(midLat, midLon),
            Distance.FromKilometers(distanceKm * 2));
    }

    /// <summary>
    /// Validates the device's current position against the active parking zones.
    /// Requests coarse location permission with a clear rationale; the location is
    /// never stored or transmitted (NFR-PR-01).
    /// </summary>
    [RelayCommand]
    private async Task ValidateLocationAsync(CancellationToken ct)
    {
        if (_config is null)
        {
            return;
        }

        ValidationMessage = null;
        try
        {
            var status = await Permissions
                .CheckStatusAsync<CoarseLocationWhenInUse>();
            if (status != PermissionStatus.Granted)
            {
                status = await Permissions
                    .RequestAsync<CoarseLocationWhenInUse>();
            }

            if (status != PermissionStatus.Granted)
            {
                SetValidation(false, L("Parking_Validate_Denied"), NoParkingColor);
                return;
            }

            var location = await _locationService.GetApproximateLocationAsync(ct);
            if (location is null)
            {
                SetValidation(false, L("Parking_Validate_Unavailable"), EventRestrictedColor);
                return;
            }

            var now = DateTimeOffset.Now;
            var zone = ParkingMap.FindZoneAtPoint(
                _config, location.Value.Latitude, location.Value.Longitude, now);
            if (zone is null)
            {
                SetValidation(false, L("Parking_Validate_OutsideZones"), EventRestrictedColor);
                return;
            }

            var kind = ParkingMap.ParseZoneKind(zone.Type);
            var allowed = ParkingMap.IsZoneAllowed(zone, _hasActiveEvent);
            var message = allowed
                ? L("Parking_Validate_Allowed").Replace("{0}", zone.Name ?? zone.Id)
                : !string.IsNullOrWhiteSpace(zone.Message)
                    ? zone.Message
                    : L(kind == ParkingZoneKind.EventRestricted
                        ? "Parking_Validate_EventRestricted"
                        : "Parking_Validate_NoParking");

            SetValidation(allowed, message, allowed ? AllowedColor : ColorFor(kind ?? ParkingZoneKind.NoParking));
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation when the page navigates away.
        }
#pragma warning disable CA1031 // Validation must always surface a user-readable outcome.
        catch (Exception)
        {
            SetValidation(false, L("Parking_Validate_Unavailable"), EventRestrictedColor);
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// Centers the map on the device's current position. Requests coarse
    /// location permission with a clear rationale; the location is never stored
    /// or transmitted (NFR-PR-01).
    /// </summary>
    [RelayCommand]
    private async Task CenterOnLocationAsync(CancellationToken ct)
    {
        MapStatusMessage = null;
        try
        {
            var status = await Permissions
                .CheckStatusAsync<CoarseLocationWhenInUse>();
            if (status != PermissionStatus.Granted)
            {
                status = await Permissions
                    .RequestAsync<CoarseLocationWhenInUse>();
            }

            if (status != PermissionStatus.Granted)
            {
                MapStatusMessage = L("Parking_Validate_Denied");
                return;
            }

            var location = await _locationService.GetApproximateLocationAsync(ct);
            if (location is null)
            {
                MapStatusMessage = L("Parking_Validate_Unavailable");
                return;
            }

            UserLocation = new Location(location.Value.Latitude, location.Value.Longitude);
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation when the page navigates away.
        }
#pragma warning disable CA1031 // Centering must always surface a user-readable outcome.
        catch (Exception)
        {
            MapStatusMessage = L("Parking_Validate_Unavailable");
        }
#pragma warning restore CA1031
    }

    private void SetValidation(bool allowed, string message, string colorHex)
    {
        ValidationIsAllowed = allowed;
        ValidationColorHex = colorHex;
        ValidationMessage = message;
    }

    private static string ZoneTypeName(ParkingZoneKind kind) => kind switch
    {
        ParkingZoneKind.Allowed => L("Parking_Legend_Allowed"),
        ParkingZoneKind.EventRestricted => L("Parking_Legend_EventRestricted"),
        ParkingZoneKind.NoParking => L("Parking_Legend_NoParking"),
        _ => string.Empty,
    };

    private static string ColorFor(ParkingZoneKind kind) => kind switch
    {
        ParkingZoneKind.Allowed => AllowedColor,
        ParkingZoneKind.EventRestricted => EventRestrictedColor,
        ParkingZoneKind.NoParking => NoParkingColor,
        _ => AllowedColor,
    };

    private static string L(string key) => LocalizationManager.Instance[key];
}
