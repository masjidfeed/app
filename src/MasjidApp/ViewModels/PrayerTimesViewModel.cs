using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasjidApp.Core.Http;
using MasjidApp.Core.Observability;
using MasjidApp.Core.PrayerTimes;
using MasjidApp.Core.Services;
using MasjidApp.Services;
using SalahAPI.Calculations;

namespace MasjidApp.ViewModels;

// FR-PT-01, FR-PT-02, FR-PT-03, FR-PT-05, FR-PT-06, FR-PT-09, NFR-OF-01

/// <summary>
/// ViewModel for the Prayer Times screen.
/// Loads today's schedule and a 30-day forward range, and drives a live countdown
/// to the next iqama, or athan when iqama is unavailable.
/// </summary>
public sealed partial class PrayerTimesViewModel : ViewModelBase
{
    private const int ScheduleWindowDays = 30;

    // Shared key with SettingsViewModel so the same preference is read by both.
    private const string TimeFormatKey = "settings_24h";

    private readonly IPrayerTimesProvider _provider;
    private readonly IUserPreferences _preferences;
    private readonly IMasjidContext _context;
    private IDispatcherTimer? _countdownTimer;
    private bool _hasLoadedOnce;

    /// <summary>Active masjid name shown above today's date.</summary>
    [ObservableProperty]
    private string _masjidName = "Masjid";

    /// <summary>Gregorian date and Hijri date shown together in the page header.</summary>
    [ObservableProperty]
    private string _todayDateText = string.Empty;

    /// <summary>Fully-resolved prayer schedule for the current calendar day.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasJumuah))]
    private DailyPrayerSchedule? _todaySchedule;

    /// <summary>Display name of the next upcoming iqama, or athan when iqama is unavailable (FR-PT-02).</summary>
    [ObservableProperty]
    private string _nextPrayerName = string.Empty;

    /// <summary>Time remaining until the next iqama, or athan when iqama is unavailable (FR-PT-02).</summary>
    [ObservableProperty]
    private TimeSpan _countdownToNextPrayer;

    /// <summary>Display name of the prayer period currently active for sky-themed countdown styling.</summary>
    private string _currentPrayerName = string.Empty;

    /// <summary>Display name of the prayer period currently active for sky-themed countdown styling.</summary>
    public string CurrentPrayerName
    {
        get => _currentPrayerName;
        private set => SetProperty(ref _currentPrayerName, value);
    }

    /// <summary>Whether times should be rendered in 24-hour format (FR-ST-03).</summary>
    [ObservableProperty]
    private bool _is24HourTime;

    /// <summary>Offline/stale state derived from the most recent API result (NFR-OF-02).</summary>
    [ObservableProperty]
    private ContentState _contentState = new(false, null, false);

    /// <summary>Prayer rows for today's schedule, including the currently active prayer.</summary>
    public ObservableCollection<TodayPrayerRow> TodayPrayerRows { get; } = [];

    /// <summary>Formatted Jumuah rows for today's schedule.</summary>
    public ObservableCollection<JumuahPrayerRow> JumuahPrayerRows { get; } = [];

    /// <summary>Prayer schedules for today plus the next 29 calendar days.</summary>
    public ObservableCollection<PrayerScheduleDayRow> Next30DaySchedules { get; } = [];

    private List<DailyPrayerSchedule> CountdownSchedules { get; } = [];

    /// <summary><c>true</c> when today has Jumuah sessions to display (FR-PT-03).</summary>
    public bool HasJumuah => JumuahPrayerRows.Count > 0;

    /// <summary>
    /// Creates the ViewModel with its required dependencies.
    /// </summary>
    public PrayerTimesViewModel(
        IPrayerTimesProvider provider,
        IUserPreferences preferences,
        IMasjidContext context)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(context);
        _provider = provider;
        _preferences = preferences;
        _context = context;
        Title = "Prayer Times";
    }

    /// <summary>
    /// Loads today's prayer schedule and the 30-day forward range.
    /// Bound to the page's <c>OnAppearing</c> event and pull-to-refresh.
    /// </summary>
    [RelayCommand]
    private Task LoadAsync(CancellationToken ct) =>
        LoadDataAsync(ApiCachePolicy.PreferCache, ct);

    /// <summary>Reloads prayer data while bypassing the 24-hour cache window.</summary>
    [RelayCommand]
    private Task RefreshAsync(CancellationToken ct) =>
        LoadDataAsync(ApiCachePolicy.ForceRefresh, ct);

    private async Task LoadDataAsync(ApiCachePolicy cachePolicy, CancellationToken ct)
    {
        // Match Home behavior: cached re-entry should not animate a refresh or rebuild
        // unchanged rows, while an explicit pull-to-refresh still shows progress.
        var showBusy = cachePolicy == ApiCachePolicy.ForceRefresh || !_hasLoadedOnce;
        if (showBusy)
        {
            IsBusy = true;
        }

        try
        {
            var previousTimeFormat = Is24HourTime;
            Is24HourTime = _preferences.GetValue(TimeFormatKey, false);

            var configResult = await _context.GetConfigAsync(cachePolicy, ct);
            var config = configResult?.ValueOrDefault;
            MasjidName = config?.Masjid?.Name ?? _context.ActiveEntry?.Name ?? "Masjid";

            var today = DateOnly.FromDateTime(DateTime.Today);

            // Load today (carries freshness state).
            var todayResult = await _provider.GetTodayAsync(cachePolicy, ct);
            ContentState = ContentState.FromApiResult(todayResult);

            if (todayResult.ValueOrDefault is DailyPrayerSchedule schedule)
            {
                var todayChanged = !SchedulesEqual(TodaySchedule, schedule);
                TodaySchedule = schedule;
                today = schedule.Date;
                TodayDateText = FormatDateLine(schedule.Date);

                if (todayChanged || previousTimeFormat != Is24HourTime)
                {
                    RebuildTodayPrayerRows();
                    RebuildJumuahRows();
                }
            }

            // Load 30-day range for the schedule view and countdown rollover.
            var range = await _provider.GetRangeAsync(
                today,
                today.AddDays(ScheduleWindowDays - 1),
                ct: ct);
            if (previousTimeFormat != Is24HourTime || !ScheduleListsEqual(CountdownSchedules, range))
            {
                CountdownSchedules.Clear();
                Next30DaySchedules.Clear();
                foreach (var day in range)
                {
                    CountdownSchedules.Add(day);
                    Next30DaySchedules.Add(PrayerScheduleDayRow.FromSchedule(day, today, Is24HourTime));
                }
            }

            UpdateCountdown();
            _hasLoadedOnce = true;
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation when the page navigates away.
        }
        finally
        {
            if (showBusy)
            {
                IsBusy = false;
            }
        }
    }

    private static bool ScheduleListsEqual(
        List<DailyPrayerSchedule> left,
        IReadOnlyList<DailyPrayerSchedule> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!SchedulesEqual(left[index], right[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool SchedulesEqual(DailyPrayerSchedule? left, DailyPrayerSchedule? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        return left is not null
            && right is not null
            && left.Date == right.Date
            && left.FajrAthan == right.FajrAthan
            && left.FajrIqama == right.FajrIqama
            && left.Sunrise == right.Sunrise
            && left.DhuhrAthan == right.DhuhrAthan
            && left.DhuhrIqama == right.DhuhrIqama
            && left.AsrAthan == right.AsrAthan
            && left.AsrIqama == right.AsrIqama
            && left.MaghribAthan == right.MaghribAthan
            && left.MaghribIqama == right.MaghribIqama
            && left.IshaAthan == right.IshaAthan
            && left.IshaIqama == right.IshaIqama
            && left.Timezone == right.Timezone
            && left.JumuahTimes.SequenceEqual(right.JumuahTimes);
    }

    /// <summary>
    /// Starts the per-second countdown timer. Call from <c>OnAppearing</c>.
    /// </summary>
    /// <param name="timer">
    /// A fresh <see cref="IDispatcherTimer"/> obtained from
    /// <c>Application.Current.Dispatcher.CreateTimer()</c>.
    /// </param>
    public void StartCountdownTimer(IDispatcherTimer timer)
    {
        ArgumentNullException.ThrowIfNull(timer);
        _countdownTimer = timer;
        _countdownTimer.Interval = TimeSpan.FromSeconds(1);
        _countdownTimer.Tick += OnCountdownTick;
        _countdownTimer.Start();
    }

    /// <summary>
    /// Stops and disposes the countdown timer. Call from <c>OnDisappearing</c>.
    /// </summary>
    public void StopCountdownTimer()
    {
        if (_countdownTimer is not null)
        {
            _countdownTimer.Stop();
            _countdownTimer.Tick -= OnCountdownTick;
            _countdownTimer = null;
        }
    }

    private void OnCountdownTick(object? sender, EventArgs e) => UpdateCountdown();

    private void UpdateCountdown()
    {
        if (TodaySchedule is null)
        {
            return;
        }

        var schedules = CountdownSchedules.Count > 0 ? CountdownSchedules : [TodaySchedule];
        var now = DateTimeOffset.UtcNow;
        var target = PrayerCountdownSelector.NextTransition(schedules, now);
        CurrentPrayerName = PrayerCountdownSelector.CurrentPrayerName(schedules, now) ?? string.Empty;
        UpdateCurrentPrayerRows(PrayerCountdownSelector.CurrentPrayerPeriodName(schedules, now));
        if (target is not null)
        {
            NextPrayerName = target.Name;
            CountdownToNextPrayer = target.TimeRemaining;
        }
    }

    private void RebuildTodayPrayerRows()
    {
        TodayPrayerRows.Clear();
        if (TodaySchedule is null)
        {
            return;
        }

        TodayPrayerRows.Add(new TodayPrayerRow("Fajr", TodaySchedule.FajrAthan, TodaySchedule.FajrIqama, Is24HourTime));
        TodayPrayerRows.Add(new TodayPrayerRow("Sunrise", TodaySchedule.Sunrise, null, Is24HourTime));
        TodayPrayerRows.Add(new TodayPrayerRow("Dhuhr", TodaySchedule.DhuhrAthan, TodaySchedule.DhuhrIqama, Is24HourTime));
        TodayPrayerRows.Add(new TodayPrayerRow("Asr", TodaySchedule.AsrAthan, TodaySchedule.AsrIqama, Is24HourTime));
        TodayPrayerRows.Add(new TodayPrayerRow("Maghrib", TodaySchedule.MaghribAthan, TodaySchedule.MaghribIqama, Is24HourTime));
        TodayPrayerRows.Add(new TodayPrayerRow("Isha", TodaySchedule.IshaAthan, TodaySchedule.IshaIqama, Is24HourTime));
    }

    private void RebuildJumuahRows()
    {
        JumuahPrayerRows.Clear();
        if (TodaySchedule is not null)
        {
            foreach (var jumuah in TodaySchedule.JumuahTimes)
            {
                JumuahPrayerRows.Add(new JumuahPrayerRow(jumuah, Is24HourTime));
            }
        }

        OnPropertyChanged(nameof(HasJumuah));
    }

    private void UpdateCurrentPrayerRows(string? prayerName)
    {
        foreach (var row in TodayPrayerRows)
        {
            row.IsCurrent = string.Equals(row.Name, prayerName, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string FormatDateLine(DateOnly date)
    {
        var dayStart = date.ToDateTime(TimeOnly.MinValue);
        var gregorian = dayStart.ToString("dddd, MMMM d, yyyy", CultureInfo.CurrentCulture);
        var hijri = HijriDateConverter.ConvertToHijri(dayStart);
        return $"{gregorian} • {hijri.Day} {MonthName(hijri.Month)} {hijri.Year}";
    }

    private static string MonthName(int month) => month switch
    {
        1 => "Muharram",
        2 => "Safar",
        3 => "Rabi' al-Awwal",
        4 => "Rabi' al-Thani",
        5 => "Jumada al-Awwal",
        6 => "Jumada al-Thani",
        7 => "Rajab",
        8 => "Sha'ban",
        9 => "Ramadan",
        10 => "Shawwal",
        11 => "Dhul-Qa'dah",
        12 => "Dhul-Hijjah",
        _ => month.ToString(CultureInfo.InvariantCulture),
    };
}

/// <summary>Presentation row for today's prayer-time list.</summary>
public sealed partial class TodayPrayerRow : ObservableObject
{
    private readonly bool _use24HourTime;

    public TodayPrayerRow(string name, TimeOnly athan, TimeOnly? iqama, bool use24HourTime)
    {
        Name = name;
        Athan = athan;
        Iqama = iqama;
        _use24HourTime = use24HourTime;
    }

    public string Name { get; }

    /// <summary>Dark-on-light prayer glyph used in light mode.</summary>
    public string IconSource => $"prayer_{Name.ToLowerInvariant()}.png";

    /// <summary>White prayer glyph used in dark mode so the icon contrasts with the background.</summary>
    public string IconSourceDark => $"prayer_{Name.ToLowerInvariant()}_white.png";

    public TimeOnly Athan { get; }

    public TimeOnly? Iqama { get; }

    public string AthanText => PrayerTimeFormatter.Format(Athan, _use24HourTime, includeMeridiem: true);

    public string IqamaText => PrayerTimeFormatter.Format(Iqama, _use24HourTime, includeMeridiem: true);

    [ObservableProperty]
    private bool _isCurrent;
}

/// <summary>Presentation row for today's Jumuah sessions.</summary>
public sealed class JumuahPrayerRow
{
    public JumuahPrayerRow(JumuahTime jumuahTime, bool use24HourTime)
    {
        Name = jumuahTime.Name;
        Location = jumuahTime.Location;
        TimeText = PrayerTimeFormatter.Format(jumuahTime.Time, use24HourTime, includeMeridiem: true);
    }

    public string Name { get; }

    public string? Location { get; }

    public string TimeText { get; }
}

/// <summary>Presentation row for the 30-day prayer-time table.</summary>
public sealed class PrayerScheduleDayRow
{
    private PrayerScheduleDayRow(DailyPrayerSchedule schedule, DateOnly today, bool use24HourTime)
    {
        Date = schedule.Date;
        DateText = schedule.Date.ToString("ddd M/d", CultureInfo.CurrentCulture);
        IsToday = schedule.Date == today;
        IsFriday = schedule.Date.DayOfWeek == DayOfWeek.Friday;
        FajrAthanText = FormatCompactTime(schedule.FajrAthan, use24HourTime);
        FajrIqamaText = FormatCompactTime(schedule.FajrIqama, use24HourTime);
        DhuhrAthanText = FormatCompactTime(schedule.DhuhrAthan, use24HourTime);
        DhuhrIqamaText = FormatCompactTime(schedule.DhuhrIqama, use24HourTime);
        AsrAthanText = FormatCompactTime(schedule.AsrAthan, use24HourTime);
        AsrIqamaText = FormatCompactTime(schedule.AsrIqama, use24HourTime);
        MaghribAthanText = FormatCompactTime(schedule.MaghribAthan, use24HourTime);
        MaghribIqamaText = FormatCompactTime(schedule.MaghribIqama, use24HourTime);
        IshaAthanText = FormatCompactTime(schedule.IshaAthan, use24HourTime);
        IshaIqamaText = FormatCompactTime(schedule.IshaIqama, use24HourTime);
    }

    public DateOnly Date { get; }

    public string DateText { get; }

    public bool IsToday { get; }

    public bool IsFriday { get; }

    public string FajrAthanText { get; }

    public string FajrIqamaText { get; }

    public string DhuhrAthanText { get; }

    public string DhuhrIqamaText { get; }

    public string AsrAthanText { get; }

    public string AsrIqamaText { get; }

    public string MaghribAthanText { get; }

    public string MaghribIqamaText { get; }

    public string IshaAthanText { get; }

    public string IshaIqamaText { get; }

    public static PrayerScheduleDayRow FromSchedule(DailyPrayerSchedule schedule, DateOnly today, bool use24HourTime) =>
        new(schedule, today, use24HourTime);

    private static string FormatCompactTime(TimeOnly? time, bool use24HourTime) =>
        PrayerTimeFormatter.Format(time, use24HourTime, includeMeridiem: false);
}
