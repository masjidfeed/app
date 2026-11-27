using MasjidApp.Core.PrayerTimes;
#if IOS
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MasjidApp.Localization;
using MasjidApp.Push;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ObjCRuntime;
#endif

namespace MasjidApp.Notifications;

public sealed partial class IqamaLiveActivityScheduler
{
#if IOS
    private const int AdditionalLeadMinutes = 10;
    private static readonly TokenCallback OnToken = RegisterToken;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _registrations = new(StringComparer.Ordinal);
    private readonly IServiceProvider _services;
    private readonly ILogger<IqamaLiveActivityScheduler> _logger;
    private long _revision;

    public IqamaLiveActivityScheduler(
        IServiceProvider services,
        ILogger<IqamaLiveActivityScheduler> logger)
    {
        _services = services;
        _logger = logger;
    }
#endif

#if !IOS
#pragma warning disable CA1822 // Keep the injectable scheduler's no-op instance methods on other platforms.
#endif
    internal void Sync(IReadOnlyList<DailyPrayerSchedule> schedules, string? masjidId, int minutesBefore)
    {
#if IOS
        if (!OperatingSystem.IsIOSVersionAtLeast(26))
        {
            return;
        }

        var localization = LocalizationManager.Instance;
        var now = DateTimeOffset.UtcNow;
        var slots = new List<IqamaActivitySlot>();
        foreach (var schedule in schedules)
        {
            var zone = PrayerReminderScheduler.ResolveTimeZone(schedule.Timezone);
            (string Prayer, TimeOnly? Time)[] iqamas =
            [
                ("Fajr", schedule.FajrIqama),
                ("Dhuhr", schedule.DhuhrIqama),
                ("Asr", schedule.AsrIqama),
                ("Maghrib", schedule.MaghribIqama),
                ("Isha", schedule.IshaIqama),
            ];
            foreach (var (prayer, time) in iqamas)
            {
                if (!time.HasValue)
                {
                    continue;
                }

                var iqama = PrayerReminderScheduler.ToDateTimeOffset(schedule.Date, time.Value, zone).ToUniversalTime();
                if (iqama.AddMinutes(1) <= now)
                {
                    continue;
                }

                var start = iqama.AddMinutes(-minutesBefore - (double)AdditionalLeadMinutes);
                var name = localization.GetString("Prayer_" + prayer);
                var id = FormattableString.Invariant($"{masjidId}/{prayer}/{iqama.ToUnixTimeSeconds()}/{start.ToUnixTimeSeconds()}");
                slots.Add(new IqamaActivitySlot(
                    id, name, start.ToUnixTimeSeconds(), iqama.ToUnixTimeSeconds(),
                    localization.GetString("Notification_PrayerReminder_Title", name),
                    localization.GetString("Notification_IqamaLiveActivity_Body")));
            }
        }

        var ordered = slots.OrderBy(slot => slot.Iqama).ToArray();
        Apply(JsonSerializer.Serialize(ordered, IqamaActivityJsonContext.Default.IqamaActivitySlotArray), OnToken);
#endif
    }

    internal void Cancel()
    {
#if IOS
        if (OperatingSystem.IsIOSVersionAtLeast(26))
        {
            Apply("[]", null);
        }
#endif
    }
#if !IOS
#pragma warning restore CA1822
#endif

#if IOS
    private void Apply(string json, TokenCallback? callback) =>
        SyncNative(Encoding.UTF8.GetBytes(json + '\0'), Interlocked.Increment(ref _revision), callback);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void TokenCallback(IntPtr token, long iqama);

    [DllImport("__Internal", EntryPoint = "MasjidAppSyncIqamaActivities")]
    private static extern void SyncNative(byte[] slots, long revision, TokenCallback? callback);

    [MonoPInvokeCallback(typeof(TokenCallback))]
    private static void RegisterToken(IntPtr token, long iqama)
    {
        var scheduler = IPlatformApplication.Current?.Services.GetService<IqamaLiveActivityScheduler>();
        if (scheduler is not null && Marshal.PtrToStringUTF8(token) is { Length: > 0 } value)
        {
            _ = scheduler.RegisterAsync(value, DateTimeOffset.FromUnixTimeSeconds(iqama));
        }
    }

    private async Task RegisterAsync(string token, DateTimeOffset iqama)
    {
        var now = DateTimeOffset.UtcNow;
        var end = iqama.AddMinutes(1);
        foreach (var registration in _registrations)
        {
            if (registration.Value <= now)
            {
                _registrations.TryRemove(registration);
            }
        }

        if (end <= now || !_registrations.TryAdd(token, end))
        {
            return;
        }

        try
        {
            // The coordinator routes deep links through HomeShell, which depends on this scheduler.
            // Resolve it only when ActivityKit supplies a token, after startup has constructed HomeShell.
            var push = _services.GetRequiredService<IPushNotificationCoordinator>();
            if (await push.RegisterIqamaActivityAsync(token, iqama).ConfigureAwait(false))
            {
                return;
            }
        }
        catch (Exception ex)
        {
            LogRegistrationFailed(_logger, ex);
        }

        _registrations.TryRemove(token, out _);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not register an iqama Live Activity for updates.")]
    private static partial void LogRegistrationFailed(ILogger logger, Exception exception);
#endif
}

#if IOS
internal sealed record IqamaActivitySlot(string Id, string Prayer, long CountdownStart, long Iqama,
    string AlertTitle, string AlertBody);

internal sealed record IqamaActivityRegistration(string ActivityToken, string FcmToken, long Iqama);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(IqamaActivitySlot[]))]
[JsonSerializable(typeof(IqamaActivityRegistration))]
internal sealed partial class IqamaActivityJsonContext : JsonSerializerContext;
#endif
