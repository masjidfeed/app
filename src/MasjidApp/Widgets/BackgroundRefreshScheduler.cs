// TC-04, FR-WG-04: periodically regenerates the widget snapshot via the OS
// background scheduler so the widget stays fresh beyond the next-day window the
// last on-screen refresh produced.

#if IOS
using System.Diagnostics;
using BackgroundTasks;
using Foundation;
#elif ANDROID
using Android.App;
using Android.App.Job;
using Android.Content;
using MasjidApp.Platforms.Android.Widget;
#endif

namespace MasjidApp.Widgets;

#if IOS

/// <summary>
/// iOS implementation backed by <see cref="BGTaskScheduler"/>.  The OS decides the
/// exact run time opportunistically within its power budget; we only request a
/// minimum cadence (FR-WG-04).
/// </summary>
public sealed class BackgroundRefreshScheduler : IBackgroundRefreshScheduler
{
    // Must match the entry in Info.plist > BGTaskSchedulerPermittedIdentifiers.
    internal const string TaskIdentifier = "com.goodsoftware.masjidapp.widgetrefresh";

    private static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(30);

    private readonly WidgetRefreshService _refresh;
    private bool _registered;

    /// <summary>Initialises a new instance of <see cref="BackgroundRefreshScheduler"/>.</summary>
    public BackgroundRefreshScheduler(WidgetRefreshService refresh) => _refresh = refresh;

    /// <inheritdoc />
    public void Register()
    {
        if (_registered)
        {
            return;
        }

        // Registration must complete before app launch finishes.
        _registered = BGTaskScheduler.Shared.Register(TaskIdentifier, null, HandleTask);
    }

    /// <inheritdoc />
    public void Schedule()
    {
        var request = new BGAppRefreshTaskRequest(TaskIdentifier)
        {
            EarliestBeginDate = (NSDate)DateTime.UtcNow.Add(MinInterval),
        };

        if (!BGTaskScheduler.Shared.Submit(request, out var error))
        {
            Debug.WriteLine($"[D1] Failed to schedule widget refresh: {error?.LocalizedDescription}");
        }
    }

    private void HandleTask(BGTask task)
    {
        // Queue the next run first so the cadence continues even if this run fails.
        Schedule();

        var cts = new CancellationTokenSource();
        task.ExpirationHandler = () => cts.Cancel();

        _ = Task.Run(async () =>
        {
            var success = true;
            try
            {
                await _refresh.RefreshAsync(cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                success = false;
                Debug.WriteLine($"[D1] Background widget refresh failed: {ex.Message}");
            }
            finally
            {
                task.SetTaskCompleted(success);
                cts.Dispose();
            }
        });
    }
}

#elif ANDROID

/// <summary>
/// Android implementation backed by <see cref="JobScheduler"/>.  A periodic job
/// (clamped by the OS to a 15-minute minimum) runs
/// <see cref="WidgetRefreshJobService"/>, which regenerates the snapshot
/// (FR-WG-04).
/// </summary>
public sealed class BackgroundRefreshScheduler : IBackgroundRefreshScheduler
{
    internal const int JobId = 0x4D41; // "MA"

    private static readonly long IntervalMs = (long)TimeSpan.FromMinutes(15).TotalMilliseconds;

    /// <summary>Initialises a new instance of <see cref="BackgroundRefreshScheduler"/>.</summary>
    public BackgroundRefreshScheduler(WidgetRefreshService refresh) => _ = refresh;

    /// <inheritdoc />
    public void Register() => Schedule();

    /// <inheritdoc />
    public void Schedule()
    {
        var context = Android.App.Application.Context;
        if (context.GetSystemService(Context.JobSchedulerService) is not JobScheduler scheduler)
        {
            return;
        }

        // Keep an already-scheduled periodic job rather than resetting its period
        // on every launch.
        foreach (var job in scheduler.AllPendingJobs ?? new List<JobInfo>())
        {
            if (job?.Id == JobId)
            {
                return;
            }
        }

        var component = new ComponentName(
            context,
            Java.Lang.Class.FromType(typeof(WidgetRefreshJobService)));

        var builder = new JobInfo.Builder(JobId, component);
        builder.SetPeriodic(IntervalMs);
        builder.SetPersisted(false);

        var jobInfo = builder.Build();
        if (jobInfo is not null)
        {
            scheduler.Schedule(jobInfo);
        }
    }
}

#else

/// <summary>No-op implementation for platforms without home-screen widgets.</summary>
public sealed class BackgroundRefreshScheduler : IBackgroundRefreshScheduler
{
    /// <summary>Initialises a new instance of <see cref="BackgroundRefreshScheduler"/>.</summary>
    public BackgroundRefreshScheduler(WidgetRefreshService refresh) => _ = refresh;

    /// <inheritdoc />
    public void Register()
    {
    }

    /// <inheritdoc />
    public void Schedule()
    {
    }
}

#endif
