using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasjidApp.Core.Utils;
using MasjidApp.PlatformPermissions;
using MasjidApp.Services;

namespace MasjidApp.ViewModels;

// FR-CM-03, NFR-PR-01, NFR-AC-01: Qibla compass using device location and magnetometer.

/// <summary>
/// ViewModel for the Qibla compass screen.
/// Computes the great-circle bearing from the device's current location to the
/// Kaaba in Mecca (21.4225°N, 39.8262°E), then combines it with the live compass
/// heading to drive the needle rotation displayed in the UI.
///
/// Location is requested with a clear rationale and used only for this bearing
/// calculation - it is never stored or transmitted (NFR-PR-01).
/// </summary>
public sealed partial class QiblaViewModel : ViewModelBase
{
    /// <summary>
    /// Static great-circle bearing from the device location to Mecca,
    /// in degrees (0–360, clockwise from north).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedleRotation))]
    private double _bearingDegrees;

    /// <summary>
    /// Live magnetic-north heading from the device compass sensor,
    /// in degrees (0–360).  Updated by <see cref="OnCompassReadingChanged"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedleRotation))]
    [NotifyPropertyChangedFor(nameof(DialRotation))]
    private double _compassHeading;

    /// <summary>
    /// Rotation to apply to the golden chevron so it points toward the Kaaba.
    /// Derived as <c>BearingDegrees − CompassHeading</c>, normalised to 0–360° (B7).
    /// </summary>
    public double NeedleRotation => QiblaCalculations.NeedleRotation(BearingDegrees, CompassHeading);

    /// <summary>
    /// Rotation to apply to the N/S/E/W ring so cardinal labels track real-world
    /// directions as the device rotates.  Equals <c>−CompassHeading</c>, normalised to 0–360° (B7).
    /// </summary>
    public double DialRotation => QiblaCalculations.DialRotation(CompassHeading);

    /// <summary><c>true</c> when location permission has been granted.</summary>
    [ObservableProperty]
    private bool _isLocationGranted;

    /// <summary><c>true</c> when the device has a supported compass sensor.</summary>
    [ObservableProperty]
    private bool _hasSensor;

    /// <summary>Human-readable status / instruction text for the user.</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    private readonly ILocationService _locationService;

    /// <summary>
    /// Creates the ViewModel.
    /// </summary>
    public QiblaViewModel(ILocationService locationService)
    {
        _locationService = locationService;
        Title = "Qibla Direction";
    }

    /// <summary>
    /// Requests location permission, fetches the device position, computes the
    /// bearing, and starts the compass sensor.
    /// Called on page appearing.
    /// </summary>
    [RelayCommand]
    private async Task LoadAsync(CancellationToken ct)
    {
        IsBusy = true;
        StatusMessage = string.Empty;

        try
        {
            // FR-CM-03, NFR-PR-01: request approximate (coarse) location only, with a
            // clear rationale. Qibla needs just an approximate position, so we avoid
            // requesting fine-location access (see CoarseLocationPermission).
            var status = await Permissions
                .CheckStatusAsync<CoarseLocationWhenInUse>();

            if (status != PermissionStatus.Granted)
            {
                status = await Permissions
                    .RequestAsync<CoarseLocationWhenInUse>();
            }

            if (status != PermissionStatus.Granted)
            {
                IsLocationGranted = false;
                StatusMessage =
                    "Location permission was denied. Please enable it in Settings to use Qibla.";
                return;
            }

            IsLocationGranted = true;
            StatusMessage = "Locating…";

            var location = await _locationService
                .GetApproximateLocationAsync(ct);

            if (location is null)
            {
                StatusMessage = "Could not determine your location. Please try again.";
                return;
            }

            BearingDegrees = QiblaCalculations.ComputeBearing(location.Value.Latitude, location.Value.Longitude);
            StatusMessage = $"Qibla direction: {BearingDegrees:F1}°";

            // Start the compass sensor so the needle tracks device rotation.
            HasSensor = Compass.Default.IsSupported;
            if (HasSensor && !Compass.Default.IsMonitoring)
            {
                Compass.Default.ReadingChanged += OnCompassReadingChanged;
                Compass.Default.Start(SensorSpeed.UI);
            }
        }
        catch (FeatureNotSupportedException)
        {
            HasSensor = false;
            StatusMessage = "Compass sensor is not available on this device.";
        }
        catch (OperationCanceledException)
        {
            // Page navigated away before location resolved - suppress silently.
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Stops the compass sensor.  Call from <c>OnDisappearing</c> to release
    /// the hardware resource when the page is not visible.
    /// </summary>
    public void StopCompass()
    {
        if (HasSensor && Compass.Default.IsMonitoring)
        {
            Compass.Default.ReadingChanged -= OnCompassReadingChanged;
            Compass.Default.Stop();
        }
    }

    private void OnCompassReadingChanged(object? sender, CompassChangedEventArgs e)
    {
        // Dispatch to the main thread so the binding update is safe (NFR-AC-01).
        var heading = e.Reading.HeadingMagneticNorth;
        MainThread.BeginInvokeOnMainThread(() => CompassHeading = heading);
    }

}

