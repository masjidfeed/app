using System.Diagnostics;
using MasjidApp.Core.Observability;
using MasjidApp.Services;
using Microsoft.Extensions.Logging;

namespace MasjidApp.Observability;

// NFR-OB-01: opt-in telemetry gated behind explicit user consent.

/// <summary>
/// Opt-in telemetry gated behind explicit user consent (NFR-OB-01).
/// Events are emitted as OpenTelemetry activities and errors as structured
/// log records via <see cref="ILogger"/>; both flow through the OpenTelemetry
/// pipeline configured in <c>MauiProgram</c>. Recording is suppressed entirely
/// when consent is not given so nothing is captured for export.
/// </summary>
public sealed partial class TelemetryService
{
    private readonly IUserPreferences _prefs;
    private readonly ILogger<TelemetryService> _logger;
    private const string ConsentKey = "telemetry_consent";

    /// <summary>Initialises the service with the user preferences store and logger.</summary>
    public TelemetryService(IUserPreferences prefs, ILogger<TelemetryService> logger)
    {
        _prefs = prefs;
        _logger = logger;
    }

    /// <summary><c>true</c> when the user has explicitly opted in to telemetry.</summary>
    public bool IsConsentGiven => _prefs.GetValue(ConsentKey, false);

    /// <summary>Persists the user's telemetry consent choice.</summary>
    public void SetConsent(bool value) => _prefs.SetValue(ConsentKey, value);

    /// <summary>
    /// Records a named event as an OpenTelemetry activity with optional string
    /// properties attached as tags. No-op when consent is not given.
    /// </summary>
    public void TrackEvent(string eventName, IDictionary<string, string>? properties = null)
    {
        if (!IsConsentGiven)
        {
            return;
        }

        using var activity = MasjidTelemetry.ActivitySource.StartActivity(
            eventName, ActivityKind.Internal);

        if (activity is not null && properties is not null)
        {
            foreach (var property in properties)
            {
                activity.SetTag(property.Key, property.Value);
            }
        }
    }

    /// <summary>
    /// Records a handled exception as a structured error log (captured by the
    /// OpenTelemetry logging pipeline) with optional string properties attached
    /// as a log scope. No-op when consent is not given.
    /// </summary>
    public void TrackError(Exception ex, IDictionary<string, string>? properties = null)
    {
        if (!IsConsentGiven)
        {
            return;
        }

        if (properties is { Count: > 0 })
        {
            using (_logger.BeginScope(properties))
            {
                LogTrackedError(_logger, ex);
            }
        }
        else
        {
            LogTrackedError(_logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Tracked telemetry error.")]
    private static partial void LogTrackedError(ILogger logger, Exception ex);
}
