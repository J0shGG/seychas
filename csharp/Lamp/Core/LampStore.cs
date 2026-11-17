using System.ComponentModel;
using System.Windows.Threading;
using Lamp.Services;

namespace Lamp.Core;

public sealed class LampStore : INotifyPropertyChanged
{
    readonly DispatcherTimer _tick;
    readonly HashSet<string> _chimed = [];
    int? _online;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? Changed;

    public AppState State { get; private set; }

    public LampStore(AppState state)
    {
        State = StateReducer.Hydrate(state);
        _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _tick.Tick += (_, _) =>
        {
            RefreshTime();
            if (State.Session?.Status == SessionStatus.running && Remaining <= 0)
                Dispatch(ActionKind.SessionFinished);
        };
        SyncTimer();
        ApplySideEffects(null, State);
    }

    public static LampStore Load() => new(StorageService.Load());

    public long Elapsed => State.Session is { } s ? StateReducer.GetElapsed(s, Format.NowMs()) : 0;
    public long Remaining => State.Session is { } s ? Math.Max(0, s.DurationMs - Elapsed) : 0;
    public double Progress
    {
        get
        {
            if (State.Session is null) return 0;
            var duration = State.Session.DurationMs;
            return duration <= 0 ? 0 : Math.Min(1, Elapsed / (double)duration);
        }
    }

    public string ClockText
    {
        get
        {
            var ms = State.Session is null ? State.Settings.FocusMin * 60_000L : Remaining;
            return Format.FormatClock(ms);
        }
    }

    public string SessionKindLabel
    {
        get
        {
            var session = State.Session;
            if (session is null) return "Готов";
            if (session.Kind == SessionKind.@break) return "Перерыв";
            return session.Status switch
            {
                SessionStatus.paused => "Пауза",
                SessionStatus.running => "Фокус",
                _ => "Готов",
            };
        }
    }

    public TaskItem? ActiveTask => State.Tasks.FirstOrDefault(t => t.Id == State.ActiveTaskId);
    public IEnumerable<TaskItem> OpenTasks => State.Tasks.Where(t => !t.Done);
    public IEnumerable<TaskItem> DoneTasks => State.Tasks.Where(t => t.Done).Take(8);
    public IEnumerable<Thought> VisibleThoughts => State.Thoughts.Take(5);

    public string StageTitle =>
        State.Session?.Kind == SessionKind.@break
            ? "Отпусти плечи"
            : ActiveTask?.Title ?? "Что сейчас важно?";

    public string StageHint
    {
        get
        {
            if (State.Session?.Kind == SessionKind.@break) return "Короткий свет. Потом вернёмся к делу.";
            if (ActiveTask is not null) return "Свет горит только здесь.";
            return "Выбери задачу слева или начни свободный фокус.";
        }
    }

    public string HeaderTitle =>
        State.Session?.Kind == SessionKind.@break
            ? "Перерыв"
            : ActiveTask?.Title ?? "Свободный фокус";

    public string WindowTitle =>
        State.Session?.Status == SessionStatus.running
            ? $"{ClockText} · {HeaderTitle}"
            : "Лампа";

    public long LiveFocusMs =>
        State.Session is { Kind: SessionKind.focus, Status: SessionStatus.running or SessionStatus.paused }
            ? Elapsed
            : 0;

    public DayStat Today
    {
        get
        {
            var day = StateReducer.TodayStat(State.History);
            return new DayStat
            {
                Date = day.Date,
                FocusedMs = day.FocusedMs + LiveFocusMs,
                Sessions = day.Sessions,
                CompletedTasks = day.CompletedTasks,
            };
        }
    }

    public string StreakLabel
    {
        get
        {
            var n = StateReducer.StreakDays(State.History);
            return n == 0
                ? "Начни первую сессию сегодня"
                : $"{n} {Format.Plural(n, "день", "дня", "дней")} подряд";
        }
    }

    public string DateLabel => Format.FormatDateRu();
    public int? OnlineCount => _online;
    public string? OnlineLabel =>
        _online is int n && n > 0
            ? $"{n} {Format.Plural(n, "человек", "человека", "человек")} сейчас"
            : null;

    public string IslandLabel
    {
        get
        {
            if (State.Session?.Status == SessionStatus.done)
                return State.Session.Kind == SessionKind.@break ? "Перерыв окончен" : "Круг окончен";
            if (State.Session?.Kind == SessionKind.@break) return "Перерыв";
            return ActiveTask?.Title ?? CurrentOpenTask()?.Title ?? "Лампа";
        }
    }

    public string IslandStatus
    {
        get
        {
            var session = State.Session;
            if (session is null) return "";
            if (session.Status == SessionStatus.done) return "готово";
            if (session.Status == SessionStatus.paused) return "пауза";
            return session.Kind == SessionKind.@break ? "отдых" : "идёт";
        }
    }

    public string IslandPace
    {
        get
        {
            var session = State.Session;
            if (session is null) return "Нет активного круга";
            if (session.Status == SessionStatus.done)
                return session.Kind == SessionKind.@break ? "Перерыв закончился" : "Круг закончился";
            var elapsed = Format.FormatClock(Elapsed);
            if (session.Status == SessionStatus.paused) return $"Прошло {elapsed} · на паузе";
            var end = DateTime.Now.AddMilliseconds(Remaining).ToString("HH:mm");
            return $"Прошло {elapsed} · до {end}";
        }
    }

    public string TodayLine
    {
        get
        {
            var cycles = State.CyclesToday;
            return $"Сегодня {Format.FormatMinutes(Today.FocusedMs)} · {cycles} {Format.Plural(cycles, "круг", "круга", "кругов")}";
        }
    }

    public bool IsRunning => State.Session?.Status == SessionStatus.running;
    public bool IsPaused => State.Session?.Status == SessionStatus.paused;
    public bool IsDone => State.Session?.Status == SessionStatus.done;
    public bool IsFocusLook => State.Session is { Kind: SessionKind.focus, Status: not SessionStatus.done };
    public bool CanReset => IsRunning || IsPaused;
    public bool CanCompleteActive => ActiveTask is { Done: false };

    public void Dispatch(ActionKind kind, object? payload = null)
    {
        var prev = State;
        State = StateReducer.Reduce(State, kind, payload);
        PersistAndNotify(prev);
    }

    public void PatchSettings(Action<Settings> edit)
    {
        var prev = State;
        var next = CloneSettings(State.Settings);
        edit(next);
        State = StateReducer.WithSettings(State, next);
        PersistAndNotify(prev);
    }

    public void SetOnline(int? count)
    {
        if (count == _online) return;
        _online = count;
        Raise();
    }

    public TaskItem? CurrentOpenTask()
    {
        var id = StateReducer.CurrentTaskId(State);
        return id is null ? null : State.Tasks.FirstOrDefault(t => t.Id == id);
    }

    void PersistAndNotify(AppState prev)
    {
        StorageService.Save(State);
        SyncTimer();
        ApplySideEffects(prev, State);
        Raise();
    }

    void SyncTimer()
    {
        var running = State.Session?.Status == SessionStatus.running;
        if (running && !_tick.IsEnabled) _tick.Start();
        if (!running && _tick.IsEnabled) _tick.Stop();
    }

    void RefreshTime() => Raise();

    void ApplySideEffects(AppState? prev, AppState next)
    {
        AudioService.SetVolume(next.Settings.Volume);
        var ambientOn = next.Session is { Status: SessionStatus.running, Kind: SessionKind.focus };
        AudioService.SetAmbient(ambientOn ? next.Settings.Ambient : AmbientKind.off);

        if (next.Session is { Status: SessionStatus.done } session && _chimed.Add(session.Id))
        {
            if (next.Settings.Chime) AudioService.PlayChime();
            if (next.Settings.Notify)
            {
                var title = session.Kind == SessionKind.focus ? "Сессия закончилась" : "Перерыв закончился";
                var body = session.Kind == SessionKind.focus
                    ? "Можно отдохнуть или зажечь лампу ещё раз."
                    : "Пора вернуться к задаче.";
                NotifyService.Show(title, body);
            }
        }

        if (prev?.Settings.Theme != next.Settings.Theme)
            ThemeService.Apply(next.Settings.Theme);
    }

    static Settings CloneSettings(Settings s) => new()
    {
        FocusMin = s.FocusMin,
        ShortBreakMin = s.ShortBreakMin,
        LongBreakMin = s.LongBreakMin,
        CyclesUntilLong = s.CyclesUntilLong,
        Chime = s.Chime,
        Ambient = s.Ambient,
        Volume = s.Volume,
        Notify = s.Notify,
        IslandEdge = s.IslandEdge,
        Theme = s.Theme,
    };

    void Raise()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        Changed?.Invoke();
    }
}
