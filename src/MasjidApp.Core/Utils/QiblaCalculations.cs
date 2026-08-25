namespace MasjidApp.Core.Utils;

// FR-CM-03, NFR-AC-01: Pure, platform-agnostic Qibla bearing and compass rotation helpers.
// Extracted from QiblaViewModel so the math can be unit-tested (QA-02, B7).

/// <summary>
/// Stateless helpers for computing Qibla bearing and compass rotation values.
/// All input and output angles are in degrees unless otherwise noted.
/// </summary>
public static class QiblaCalculations
{
    // Kaaba coordinates in radians (FR-CM-03).
    private const double MeccaLatRad = 21.4225 * Math.PI / 180.0;
    private const double MeccaLonRad = 39.8262 * Math.PI / 180.0;

    /// <summary>
    /// Great-circle bearing from <paramref name="fromLat"/>/<paramref name="fromLon"/>
    /// to the Kaaba in Mecca (21.4225°N, 39.8262°E), in degrees (0–360, clockwise from north).
    /// Formula: atan2(sin(Δlon)·cos(lat₂), cos(lat₁)·sin(lat₂) − sin(lat₁)·cos(lat₂)·cos(Δlon))
    /// </summary>
    public static double ComputeBearing(double fromLat, double fromLon)
    {
        var lat1 = fromLat * Math.PI / 180.0;
        var lon1 = fromLon * Math.PI / 180.0;
        var dLon = MeccaLonRad - lon1;

        var x = Math.Sin(dLon) * Math.Cos(MeccaLatRad);
        var y = (Math.Cos(lat1) * Math.Sin(MeccaLatRad))
              - (Math.Sin(lat1) * Math.Cos(MeccaLatRad) * Math.Cos(dLon));

        return Normalize360(Math.Atan2(x, y) * 180.0 / Math.PI);
    }

    /// <summary>
    /// Rotation to apply to the Qibla indicator (golden chevron) so it points toward the Kaaba.
    /// Equals <c>bearingDegrees − compassHeading</c>, normalised to 0–360°.
    /// </summary>
    public static double NeedleRotation(double bearingDegrees, double compassHeading)
        => Normalize360(bearingDegrees - compassHeading);

    /// <summary>
    /// Rotation to apply to the N/S/E/W compass ring so cardinal labels track real-world
    /// directions as the device rotates.  Equals <c>−compassHeading</c>, normalised to 0–360°.
    /// </summary>
    public static double DialRotation(double compassHeading)
        => Normalize360(-compassHeading);

    /// <summary>Normalises <paramref name="degrees"/> to the range [0, 360).</summary>
    public static double Normalize360(double degrees)
    {
        degrees %= 360.0;
        return degrees < 0.0 ? degrees + 360.0 : degrees;
    }
}
