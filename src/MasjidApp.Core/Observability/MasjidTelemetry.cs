using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace MasjidApp.Core.Observability;

// NFR-OB-01, NFR-PF-01, NFR-RL-01: OpenTelemetry-native instrumentation handles.

/// <summary>
/// Central OpenTelemetry instrumentation handles shared by the platform-agnostic core.
/// The core only emits signals through these primitives
/// (<see cref="System.Diagnostics.ActivitySource"/>, <see cref="System.Diagnostics.Metrics.Meter"/>,
/// and <see cref="Microsoft.Extensions.Logging.ILogger"/>); the application head composes the
/// OpenTelemetry SDK and exporters around them. This keeps the core free of any SDK or exporter
/// dependency while remaining fully observable.
/// </summary>
public static class MasjidTelemetry
{
    /// <summary>Shared name for the core <see cref="ActivitySource"/> and <see cref="Meter"/>.</summary>
    public const string Name = "MasjidApp";

    private static readonly string Version =
        typeof(MasjidTelemetry).Assembly.GetName().Version?.ToString() ?? "1.0.0";

    /// <summary>Activity source for distributed traces emitted by core domain logic.</summary>
    public static readonly ActivitySource ActivitySource = new(Name, Version);

    /// <summary>Meter for metrics emitted by core domain logic.</summary>
    public static readonly Meter Meter = new(Name, Version);

    /// <summary>
    /// Counts operations that caught an exception and degraded to a cached or empty
    /// result instead of failing (NFR-RL-01). Tag <c>operation</c> identifies the call site.
    /// </summary>
    public static readonly Counter<long> DegradedOperations =
        Meter.CreateCounter<long>(
            "masjidapp.degraded_operations",
            unit: "{operation}",
            description: "Operations that handled an exception and degraded gracefully.");

    /// <summary>Builds the standard <c>operation</c> tag for <see cref="DegradedOperations"/>.</summary>
    /// <param name="operation">The logical operation name (e.g. <c>prayer_time_calculation</c>).</param>
    public static KeyValuePair<string, object?> OperationTag(string operation) =>
        new("operation", operation);
}
