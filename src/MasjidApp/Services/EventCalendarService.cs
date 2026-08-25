using MasjidApp.Core.Events;

#if ANDROID
using Android.Content;
#elif IOS || MACCATALYST
using EventKit;
using EventKitUI;
using Foundation;
#endif

namespace MasjidApp.Services;

/// <summary>Native calendar editor integration for the active platform.</summary>
public sealed class EventCalendarService : IEventCalendarService, IDisposable
{
#if IOS || MACCATALYST
    private readonly SemaphoreSlim _presentationGate = new(1, 1);
    private EKEventStore? _eventStore;
    private EKEventEditViewController? _editor;
    private CalendarEditDelegate? _editorDelegate;
#endif

    /// <inheritdoc/>
    public async Task<bool> PresentAddEventAsync(CalendarEventDraft draft, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

#if ANDROID
        var intent = new Intent(Intent.ActionInsert, Android.Provider.CalendarContract.Events.ContentUri)
            .PutExtra("title", draft.Title)
            .PutExtra("eventLocation", draft.Location)
            .PutExtra("description", draft.Notes)
            .PutExtra("beginTime", draft.Start.ToUnixTimeMilliseconds())
            .PutExtra("endTime", draft.End.ToUnixTimeMilliseconds());

        var context = Platform.CurrentActivity ?? Android.App.Application.Context;
        if (Platform.CurrentActivity is null)
        {
            intent.AddFlags(ActivityFlags.NewTask);
        }

        try
        {
            context.StartActivity(intent);
            return true;
        }
        catch (ActivityNotFoundException)
        {
            return false;
        }
#elif IOS || MACCATALYST
        await _presentationGate.WaitAsync(ct);
        try
        {
            return await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var presenter = Platform.GetCurrentUIViewController();
                if (presenter is null)
                {
                    return false;
                }

                var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _eventStore = new EKEventStore();
                var calendarEvent = EKEvent.FromStore(_eventStore);
                calendarEvent.Title = draft.Title;
                calendarEvent.StartDate = NSDate.FromTimeIntervalSince1970(draft.Start.ToUnixTimeSeconds());
                calendarEvent.EndDate = NSDate.FromTimeIntervalSince1970(draft.End.ToUnixTimeSeconds());
                calendarEvent.Location = draft.Location;
                calendarEvent.Notes = draft.Notes;

                _editorDelegate = new CalendarEditDelegate(completion);
                _editor = new EKEventEditViewController
                {
                    EventStore = _eventStore,
                    Event = calendarEvent,
                    EditViewDelegate = _editorDelegate,
                };

                presenter.PresentViewController(_editor, true, null);
                return await completion.Task.WaitAsync(ct);
            });
        }
        finally
        {
            _editor?.Dispose();
            _editor = null;
            _editorDelegate?.Dispose();
            _editorDelegate = null;
            _eventStore?.Dispose();
            _eventStore = null;
            _presentationGate.Release();
        }
#else
        await Task.CompletedTask;
        return false;
#endif
    }

    /// <inheritdoc/>
    public void Dispose()
    {
#if IOS || MACCATALYST
        _editor?.Dispose();
        _editor = null;
        _editorDelegate?.Dispose();
        _editorDelegate = null;
        _eventStore?.Dispose();
        _eventStore = null;
        _presentationGate.Dispose();
#endif
    }

#if IOS || MACCATALYST
    private sealed class CalendarEditDelegate(TaskCompletionSource<bool> completion) : EKEventEditViewDelegate
    {
        public override void Completed(
            EKEventEditViewController controller,
            EKEventEditViewAction action)
        {
            controller.DismissViewController(true, () =>
                completion.TrySetResult(action == EKEventEditViewAction.Saved));
        }
    }
#endif
}
