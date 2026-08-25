using FluentAssertions;
using MasjidApp.Core.Utils;

namespace MasjidApp.Core.Tests.Utils;

// QA-02, B7: Unit tests for the pure Qibla bearing and compass rotation helpers.
public sealed class QiblaCalculationsTests
{
    // ── Normalize360 ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(90.0, 90.0)]
    [InlineData(360.0, 0.0)]
    [InlineData(450.0, 90.0)]
    [InlineData(-90.0, 270.0)]
    [InlineData(-360.0, 0.0)]
    [InlineData(-1.0, 359.0)]
    public void Normalize360_VariousInputs_ReturnsNormalisedDegrees(double input, double expected)
    {
        var result = QiblaCalculations.Normalize360(input);
        result.Should().BeApproximately(expected, precision: 0.001);
    }

    // ── NeedleRotation ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(90.0, 0.0, 90.0)]     // device pointing north, Qibla east → chevron at 90°
    [InlineData(90.0, 90.0, 0.0)]     // device pointing east, Qibla east → chevron at 0° (top)
    [InlineData(10.0, 20.0, 350.0)]   // bearing behind heading → wraps through 360
    [InlineData(0.0, 0.0, 0.0)]       // coincident → 0
    public void NeedleRotation_BearingMinusHeading_NormalisedCorrectly(
        double bearing, double heading, double expected)
    {
        var result = QiblaCalculations.NeedleRotation(bearing, heading);
        result.Should().BeApproximately(expected, precision: 0.001);
    }

    // ── DialRotation ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0.0, 0.0)]      // device pointing north → ring at 0 (N at top)
    [InlineData(90.0, 270.0)]   // device rotated 90° CW → ring counter-rotates 90° (270)
    [InlineData(180.0, 180.0)]  // device pointing south → N at bottom of ring (180)
    [InlineData(270.0, 90.0)]   // device rotated 270° CW → ring at 90°
    [InlineData(360.0, 0.0)]    // full rotation wraps to 0
    public void DialRotation_NegativeHeading_NormalisedCorrectly(
        double compassHeading, double expected)
    {
        var result = QiblaCalculations.DialRotation(compassHeading);
        result.Should().BeApproximately(expected, precision: 0.001);
    }

    // ── ComputeBearing (reference values) ──────────────────────────────────

    // Reference: bearing from London (~51.5°N, 0.1°W) to Mecca is approx 119°.
    [Fact]
    public void ComputeBearing_FromLondon_IsApprox119Degrees()
    {
        var bearing = QiblaCalculations.ComputeBearing(51.5074, -0.1278);
        bearing.Should().BeApproximately(119.0, precision: 2.0);
    }

    // From New York (~40.7°N, 74.0°W): approx 58°.
    [Fact]
    public void ComputeBearing_FromNewYork_IsApprox58Degrees()
    {
        var bearing = QiblaCalculations.ComputeBearing(40.7128, -74.0060);
        bearing.Should().BeApproximately(58.0, precision: 2.0);
    }

    // From Sydney (~33.9°S, 151.2°E): approx 277°.
    [Fact]
    public void ComputeBearing_FromSydney_IsApprox277Degrees()
    {
        var bearing = QiblaCalculations.ComputeBearing(-33.8688, 151.2093);
        bearing.Should().BeApproximately(277.0, precision: 2.0);
    }

    // Standing at the Kaaba itself should produce 0° (degenerate case - same point).
    [Fact]
    public void ComputeBearing_FromMecca_ReturnsNormalisedValue()
    {
        var bearing = QiblaCalculations.ComputeBearing(21.4225, 39.8262);
        // atan2(0,0) is implementation-defined but Normalize360 must keep it in [0,360).
        bearing.Should().BeInRange(0.0, 360.0);
    }

    // Result must always be in [0, 360).
    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(51.5074, -0.1278)]
    [InlineData(-33.8688, 151.2093)]
    [InlineData(40.7128, -74.0060)]
    public void ComputeBearing_AnyLocation_ResultInRange0To360(double lat, double lon)
    {
        var bearing = QiblaCalculations.ComputeBearing(lat, lon);
        bearing.Should().BeGreaterThanOrEqualTo(0.0).And.BeLessThan(360.0);
    }
}
