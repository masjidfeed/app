using MasjidApp.Core.Notifications;
using MasjidApp.Core.PrayerTimes;
using MasjidApp.Core.Services;
using Microsoft.Extensions.Logging;

namespace MasjidApp.Services;

/// <summary>
/// Coordinates persisted iqama reminder settings, prayer-time data, and platform scheduling.
/// </summary>
public sealed partial class PrayerReminderSettingsService : IPrayerReminderSettingsService, IDisposable
{
    private const int ScheduleWindowDays = 6;
    private const string EnabledKey = "settings_prayer_reminders";
    private const string MinutesBeforeKey = "settings_prayer_reminder_minutes";
    private const string FingerprintKey = "settings_prayer_reminder_fingerprint";

    // Shared with SettingsViewModel/PrayerTimesViewModel; the notification body embeds a
    // formatted time, so the reminder window has to follow the same 12/24-hour preference.
    private const string TimeFormatKey = "settings_24h";
    private const int DefaultMinutesBefore = 10;

    private readonly IUserPreferences _preferences;
    private readonly IPrayerTimesProvider _prayerTimesProvider;
    private readonly IPrayerReminderScheduler _scheduler;
    private readonly IMasjidContext _masjidContext;
    private readonly ILogger<PrayerReminderSettingsService> _logger;
    private readonly SemaphoreSlim _updateLock = new(1, 1);

    /// <summary>Initializes the reminder settings coordinator.</summary>
    public PrayerReminderSettingsService(
        IUserPreferences preferences,
        IPrayerTimesProvider prayerTimesProvider,
        IPrayerReminderScheduler scheduler,
        IMasjidContext masjidContext,
        ILogger<PrayerReminderSettingsService> logger)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(prayerTimesProvider);
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(masjidContext);
        ArgumentNullException.ThrowIfNull(logger);

        _preferences = preferences;
        _prayerTimesProvider = prayerTimesProvider;
        _scheduler = scheduler;
        _masjidContext = masjidContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsEnabled => _preferences.GetValue(EnabledKey, false);

    /// <inheritdoc />
    public int MinutesBefore => Math.Max(
        0,
        _preferences.GetValue(MinutesBeforeKey, DefaultMinutesBefore));

    /// <inheritdoc />
    public async Task<PrayerReminderUpdateResult> UpdateAsync(
        bool enabled,
        int minutesBefore,
        CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minutesBefore);

        // Rescheduling fetches a multi-day prayer window and issues dozens of platform
        // notification calls, so it runs off the caller's (UI) thread (FR-ST-02).
        try
        {
            return await Task.Run(() => UpdateCoreAsync(enabled, minutesBefore, ct), ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogSchedulingFailure(_logger, ex);
            return PrayerReminderUpdateResult.SchedulingFailed;
        }
    }

    private async Task<PrayerReminderUpdateResult> UpdateCoreAsync(
        bool enabled,
        int minutesBefore,
        CancellationToken ct)
    {
        await _updateLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _preferences.SetValue(EnabledKey, enabled);
            _preferences.SetValue(MinutesBeforeKey, minutesBefore);

            // An explicit settings change must always reschedule, so drop any saved
            // fingerprint before doing the work (FR-ST-02).
            InvalidateFingerprint();

            await _scheduler.CancelAllAsync(ct).ConfigureAwait(false);
            if (!enabled)
            {
                return PrayerReminderUpdateResult.Disabled;
            }

            // One range fetch covers today plus the rest of the rolling window.
            var windowStart = Today();
            var schedules = await _prayerTimesProvider.GetRangeAsync(
                windowStart,
                windowStart.AddDays(ScheduleWindowDays - 1),
                ct: ct).ConfigureAwait(false);
            if (schedules.Count == 0)
            {
                return PrayerReminderUpdateResult.ScheduleUnavailable;
            }

            return await ScheduleAsync(
                schedules,
                minutesBefore,
                ComputeFingerprint(schedules, minutesBefore, windowStart),
                ct).ConfigureAwait(false);
        }
        finally
        {
            _updateLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<PrayerReminderUpdateResult> SyncAsync(
        IReadOnlyList<DailyPrayerSchedule> schedules,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(schedules);

        try
        {
            return await Task.Run(() => SyncCoreAsync(schedules, ct), ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogSchedulingFailure(_logger, ex);
            return PrayerReminderUpdateResult.SchedulingFailed;
        }
    }

    private async Task<PrayerReminderUpdateResult> SyncCoreAsync(
        IReadOnlyList<DailyPrayerSchedule> schedules,
        CancellationToken ct)
    {
        await _updateLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!IsEnabled)
            {
                InvalidateFingerprint();
                await _scheduler.CancelAllAsync(ct).ConfigureAwait(false);
                return PrayerReminderUpdateResult.Disabled;
            }

            if (schedules.Count == 0)
            {
                InvalidateFingerprint();
                await _scheduler.CancelAllAsync(ct).ConfigureAwait(false);
                return PrayerReminderUpdateResult.ScheduleUnavailable;
            }

            var minutesBefore = MinutesBefore;
            var fingerprint = ComputeFingerprint(schedules, minutesBefore, Today());
            if (string.Equals(
                    _preferences.GetValue(FingerprintKey, string.Empty),
                    fingerprint,
                    StringComparison.Ordinal))
            {
                // Nothing that affects the pending window changed, so the already-scheduled
                // notifications stay untouched (FR-PN-07).
                return PrayerReminderUpdateResult.Unchanged;
            }

            await _scheduler.CancelAllAsync(ct).ConfigureAwait(false);
            return await ScheduleAsync(schedules, minutesBefore, fingerprint, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            _updateLock.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _updateLock.Dispose();

    private async Task<PrayerReminderUpdateResult> ScheduleAsync(
        IReadOnlyList<DailyPrayerSchedule> schedules,
        int minutesBefore,
        string fingerprint,
        CancellationToken ct)
    {
        var preferences = PrayerNotificationPreferences.AllIqamas(minutesBefore, Use24HourTime);
        var permissionGranted = await _scheduler.ScheduleAsync(schedules, preferences, ct)
            .ConfigureAwait(false);
        if (permissionGranted)
        {
            _preferences.SetValue(FingerprintKey, fingerprint);
            return PrayerReminderUpdateResult.Scheduled;
        }

        _preferences.SetValue(EnabledKey, false);
        InvalidateFingerprint();
        return PrayerReminderUpdateResult.PermissionDenied;
    }

    private bool Use24HourTime => _preferences.GetValue(TimeFormatKey, false);

    private string ComputeFingerprint(
        IReadOnlyList<DailyPrayerSchedule> schedules,
        int minutesBefore,
        DateOnly windowStart) =>
        PrayerReminderFingerprint.Compute(
            enabled: true,
            minutesBefore,
            _masjidContext.ActiveEntry?.Id,
            windowStart,
            schedules,
            Use24HourTime);

    /// <summary>
    /// Current local date, matching how <see cref="IPrayerTimesProvider"/> resolves "today".
    /// Folding it into the fingerprint guarantees exactly one window refill per day rollover.
    /// </summary>
    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.Now);

    private void InvalidateFingerprint() => _preferences.SetValue(FingerprintKey, string.Empty);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to update pending prayer reminders.")]
    private static partial void LogSchedulingFailure(ILogger logger, Exception ex);
}
