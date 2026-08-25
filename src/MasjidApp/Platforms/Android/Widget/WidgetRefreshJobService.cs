using Android.App;
using Android.App.Job;
using MasjidApp.Widgets;
using Microsoft.Extensions.DependencyInjection;

namespace MasjidApp.Platforms.Android.Widget;

// TC-04, FR-WG-04: JobScheduler-driven background regeneration of the widget
// snapshot. Scheduled by BackgroundRefreshScheduler; runs WidgetRefreshService
// off the main thread.

/// <summary>
/// Background job that regenerates the widget snapshot by invoking
/// <see cref="WidgetRefreshService.RefreshAsync"/>.  Resolves the service from the
/// MAUI DI container at run time so it works even when the app process was started
/// solely to service this job.
/// </summary>
[Service(
    Name = "com.goodsoftware.masjidapp.WidgetRefreshJobService",
    Permission = "android.permission.BIND_JOB_SERVICE",
    Exported = false)]
public sealed class WidgetRefreshJobService : JobService
{
    /// <inheritdoc />
    public override bool OnStartJob(JobParameters? @params)
    {
        var refresh = IPlatformApplication.Current?.Services.GetService<WidgetRefreshService>();
        if (refresh is null)
        {
            return false; // nothing to do; no reschedule required
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await refresh.RefreshAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[D1] Background widget refresh failed: {ex.Message}");
            }
            finally
            {
                // false: the periodic job stays scheduled and the OS will run it again.
                JobFinished(@params, wantsReschedule: false);
            }
        });

        return true; // work continues on a background thread
    }

    /// <inheritdoc />
    public override bool OnStopJob(JobParameters? @params) => true; // reschedule if interrupted
}
