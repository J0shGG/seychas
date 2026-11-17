namespace Lamp.Core;

public static class StateReducer
{
    public const long MinCreditMs = 30_000;

    public static Settings DefaultSettings() => new();

    public static AppState Empty() => new();

    public static Settings CleanSettings(Settings? raw)
    {
        var settings = raw ?? new Settings();
        if (settings.IslandEdge is not IslandEdge.top and not IslandEdge.left and not IslandEdge.right)
            settings.IslandEdge = IslandEdge.top;
        if (settings.Theme is not ThemeId.lamp and not ThemeId.light and not ThemeId.dark and not ThemeId.ink)
            settings.Theme = ThemeId.lamp;
        settings.Volume = double.IsFinite(settings.Volume) ? Math.Clamp(settings.Volume, 0, 1) : 1;
        var focus = (int)Math.Round((double)settings.FocusMin);
        settings.FocusMin = focus is >= 1 and <= 180 ? focus : 25;
        return settings;
    }

    public static long GetElapsed(Session session, long now)
    {
        if (session.Status == SessionStatus.done)
            return Math.Min(session.ElapsedMs, session.DurationMs);
        var extra = session.RunningSince is long since ? Math.Max(0, now - since) : 0;
        return session.ElapsedMs + extra;
    }

    public static AppState Hydrate(AppState raw)
    {
        var date = Format.TodayKey();
        raw.Settings = CleanSettings(raw.Settings);
        raw.Tasks ??= [];
        raw.Thoughts ??= [];
        raw.History ??= [];
        if (raw.IntentionDate != date)
        {
            raw.Intention = "";
            raw.IntentionDate = date;
        }
        if (raw.CyclesDate != date)
        {
            raw.CyclesToday = 0;
            raw.CyclesDate = date;
        }
        if (raw.Session?.Status == SessionStatus.running)
        {
            var elapsed = GetElapsed(raw.Session, Format.NowMs());
            if (elapsed >= raw.Session.DurationMs)
                return FinishSession(raw, Format.NowMs());
        }
        return raw;
    }

    public static int BreakMinutes(AppState state)
    {
        var s = state.Settings;
        if (state.CyclesToday > 0 && state.CyclesToday % s.CyclesUntilLong == 0) return s.LongBreakMin;
        return s.ShortBreakMin;
    }

    public static DayStat TodayStat(IEnumerable<DayStat> history)
    {
        var date = Format.TodayKey();
        return history.FirstOrDefault(d => d.Date == date)
            ?? new DayStat { Date = date };
    }

    public static int StreakDays(IEnumerable<DayStat> history)
    {
        var map = history.Where(d => d.FocusedMs > 0).Select(d => d.Date).ToHashSet();
        var today = Format.TodayKey();
        var start = map.Contains(today) ? today : Format.YesterdayKey();
        if (!map.Contains(start) && start != today) return 0;
        var count = 0;
        var cursor = DateTime.Now.Date;
        if (start != today) cursor = cursor.AddDays(-1);
        while (map.Contains(Format.TodayKey(cursor)))
        {
            count++;
            cursor = cursor.AddDays(-1);
        }
        return count;
    }

    public static string? CurrentTaskId(AppState state)
    {
        var active = state.Tasks.FirstOrDefault(t => t.Id == state.ActiveTaskId);
        if (active is { Done: false }) return active.Id;
        var session = state.Session;
        if (session is { Kind: SessionKind.focus, TaskId: { } id })
        {
            var task = state.Tasks.FirstOrDefault(t => t.Id == id);
            if (task is { Done: false }) return task.Id;
        }
        return null;
    }

    static string? NextOpenTaskId(List<TaskItem> tasks, string currentId)
    {
        var index = tasks.FindIndex(t => t.Id == currentId);
        for (var i = index + 1; i < tasks.Count; i++)
            if (!tasks[i].Done) return tasks[i].Id;
        for (var i = 0; i < index; i++)
            if (!tasks[i].Done) return tasks[i].Id;
        return null;
    }

    static List<DayStat> CreditDay(List<DayStat> history, string date, long focusedMs, int sessions, int completed = 0)
    {
        var next = history.Select(Clone).ToList();
        var i = next.FindIndex(d => d.Date == date);
        if (i == -1)
        {
            next.Add(new DayStat { Date = date, FocusedMs = focusedMs, Sessions = sessions, CompletedTasks = completed });
        }
        else
        {
            next[i].FocusedMs += focusedMs;
            next[i].Sessions += sessions;
            next[i].CompletedTasks += completed;
        }
        return next;
    }

    static AppState ApplyFocusCredit(AppState state, long elapsedMs)
    {
        if (elapsedMs < MinCreditMs) return state;
        var date = Format.TodayKey();
        var tasks = state.Tasks.Select(Clone).ToList();
        foreach (var t in tasks)
            if (t.Id == state.Session?.TaskId) t.FocusedMs += elapsedMs;
        return Copy(state) with
        {
            Tasks = tasks,
            History = CreditDay(state.History, date, elapsedMs, 1),
        };
    }

    public static AppState FinishSession(AppState state, long now)
    {
        var session = state.Session;
        if (session is null || session.Status == SessionStatus.done) return state;
        var elapsed = Math.Min(GetElapsed(session, now), session.DurationMs);
        var done = Clone(session);
        done.ElapsedMs = elapsed;
        done.RunningSince = null;
        done.Status = SessionStatus.done;
        var next = Copy(state) with { Session = done };
        if (session.Kind == SessionKind.focus)
        {
            next = ApplyFocusCredit(next, elapsed);
            var date = Format.TodayKey();
            var cycles = next.CyclesDate == date ? next.CyclesToday + 1 : 1;
            next = Copy(next) with { CyclesToday = cycles, CyclesDate = date };
        }
        return next;
    }

    public static AppState CompleteCurrentTask(AppState state, long now)
    {
        var currentId = CurrentTaskId(state);
        if (currentId is null) return state;
        var nextId = NextOpenTaskId(state.Tasks, currentId);
        var tasks = state.Tasks.Select(Clone).ToList();
        foreach (var item in tasks)
            if (item.Id == currentId)
            {
                item.Done = true;
                item.CompletedAt = now;
            }
        var history = CreditDay(state.History, Format.TodayKey(), 0, 0, 1);
        var session = state.Session is null ? null : Clone(state.Session);
        if (session is { Kind: SessionKind.focus, Status: not SessionStatus.done } && session.TaskId == currentId)
        {
            var elapsed = GetElapsed(session, now);
            if (elapsed >= MinCreditMs)
            {
                foreach (var item in tasks)
                    if (item.Id == currentId) item.FocusedMs += elapsed;
                history = CreditDay(history, Format.TodayKey(), elapsed, 0);
                session.TaskId = nextId;
                session.ElapsedMs = 0;
                session.RunningSince = session.Status == SessionStatus.running ? now : null;
            }
            else session.TaskId = nextId;
        }
        return Copy(state) with { Tasks = tasks, History = history, ActiveTaskId = nextId, Session = session };
    }

    static AppState StopSession(AppState state, long now)
    {
        var session = state.Session;
        if (session is null) return state;
        var elapsed = GetElapsed(session, now);
        var next = Copy(state) with { Session = null };
        if (session.Kind == SessionKind.focus && session.Status != SessionStatus.done)
        {
            next = ApplyFocusCredit(Copy(next) with { Session = session }, elapsed);
            next = Copy(next) with { Session = null };
        }
        return next;
    }

    public static AppState Reduce(AppState state, ActionKind kind, object? payload = null)
    {
        var now = Format.NowMs();
        switch (kind)
        {
            case ActionKind.CompleteOnboarding:
                return Copy(state) with { Onboarded = true };
            case ActionKind.AddTask:
            {
                var title = ((string)payload!).Trim();
                if (title.Length == 0) return state;
                var task = new TaskItem { Id = Format.Uid(), Title = title, CreatedAt = now };
                var tasks = new List<TaskItem> { task };
                tasks.AddRange(state.Tasks.Select(Clone));
                return Copy(state) with
                {
                    Tasks = tasks,
                    ActiveTaskId = state.ActiveTaskId ?? task.Id,
                };
            }
            case ActionKind.RemoveTask:
            {
                var id = (string)payload!;
                var tasks = state.Tasks.Where(t => t.Id != id).Select(Clone).ToList();
                var active = state.ActiveTaskId == id ? tasks.FirstOrDefault(t => !t.Done)?.Id : state.ActiveTaskId;
                return Copy(state) with { Tasks = tasks, ActiveTaskId = active };
            }
            case ActionKind.ToggleDone:
            {
                var id = (string)payload!;
                var tasks = state.Tasks.Select(Clone).ToList();
                var toggled = tasks.FirstOrDefault(t => t.Id == id);
                if (toggled is null) return state;
                toggled.Done = !toggled.Done;
                toggled.CompletedAt = toggled.Done ? now : null;
                var history = state.History.Select(Clone).ToList();
                if (toggled.Done) history = CreditDay(history, Format.TodayKey(), 0, 0, 1);
                var active = state.ActiveTaskId;
                if (toggled.Done && active == id) active = tasks.FirstOrDefault(t => !t.Done)?.Id;
                return Copy(state) with { Tasks = tasks, History = history, ActiveTaskId = active };
            }
            case ActionKind.CompleteCurrent:
                return CompleteCurrentTask(state, now);
            case ActionKind.SelectTask:
                return Copy(state) with { ActiveTaskId = (string?)payload };
            case ActionKind.SetIntention:
                return Copy(state) with { Intention = (string)payload!, IntentionDate = Format.TodayKey() };
            case ActionKind.StartFocus:
            case ActionKind.StartBreak:
            {
                if (state.Session is { Status: SessionStatus.running or SessionStatus.paused }) return state;
                var sessionKind = kind == ActionKind.StartBreak ? SessionKind.@break : SessionKind.focus;
                var minutes = sessionKind == SessionKind.focus ? state.Settings.FocusMin : BreakMinutes(state);
                return Copy(state) with
                {
                    Session = new Session
                    {
                        Id = Format.Uid(),
                        TaskId = sessionKind == SessionKind.focus ? state.ActiveTaskId : null,
                        Kind = sessionKind,
                        DurationMs = minutes * 60_000L,
                        StartedAt = now,
                        RunningSince = now,
                        Status = SessionStatus.running,
                    },
                };
            }
            case ActionKind.Pause:
            {
                if (state.Session?.Status != SessionStatus.running) return state;
                var session = Clone(state.Session);
                session.ElapsedMs = GetElapsed(session, now);
                session.RunningSince = null;
                session.Status = SessionStatus.paused;
                return Copy(state) with { Session = session };
            }
            case ActionKind.Resume:
            {
                if (state.Session?.Status != SessionStatus.paused) return state;
                var session = Clone(state.Session);
                session.RunningSince = now;
                session.Status = SessionStatus.running;
                return Copy(state) with { Session = session };
            }
            case ActionKind.Stop:
                return StopSession(state, now);
            case ActionKind.SessionFinished:
                return FinishSession(state, now);
            case ActionKind.AckDone:
                return Copy(state) with { Session = null };
            case ActionKind.AddThought:
            {
                var text = ((string)payload!).Trim();
                if (text.Length == 0) return state;
                var thoughts = new List<Thought> { new() { Id = Format.Uid(), Text = text, CreatedAt = now } };
                thoughts.AddRange(state.Thoughts.Select(Clone));
                return Copy(state) with { Thoughts = thoughts.Take(40).ToList() };
            }
            case ActionKind.PromoteThought:
            {
                var id = (string)payload!;
                var thought = state.Thoughts.FirstOrDefault(t => t.Id == id);
                if (thought is null) return state;
                var task = new TaskItem { Id = Format.Uid(), Title = thought.Text, CreatedAt = now };
                var tasks = new List<TaskItem> { task };
                tasks.AddRange(state.Tasks.Select(Clone));
                return Copy(state) with
                {
                    Thoughts = state.Thoughts.Where(t => t.Id != id).Select(Clone).ToList(),
                    Tasks = tasks,
                };
            }
            case ActionKind.DismissThought:
                return Copy(state) with
                {
                    Thoughts = state.Thoughts.Where(t => t.Id != (string)payload!).Select(Clone).ToList(),
                };
            case ActionKind.PatchSettings:
            {
                var patch = (Settings)payload!;
                var merged = Clone(state.Settings);
                if (patch.FocusMin != 0) merged.FocusMin = patch.FocusMin;
                return Copy(state) with { Settings = CleanSettings(MergeSettings(state.Settings, patch)) };
            }
            default:
                return state;
        }
    }

    public static Settings MergeSettings(Settings current, Settings patch)
    {
        return new Settings
        {
            FocusMin = patch.FocusMin != 0 ? patch.FocusMin : current.FocusMin,
            ShortBreakMin = patch.ShortBreakMin != 0 ? patch.ShortBreakMin : current.ShortBreakMin,
            LongBreakMin = patch.LongBreakMin != 0 ? patch.LongBreakMin : current.LongBreakMin,
            CyclesUntilLong = patch.CyclesUntilLong != 0 ? patch.CyclesUntilLong : current.CyclesUntilLong,
            Chime = patch.Chime,
            Ambient = patch.Ambient,
            Volume = patch.Volume,
            Notify = patch.Notify,
            IslandEdge = patch.IslandEdge,
            Theme = patch.Theme,
        };
    }

    // PatchSettings above is messy because bools can't use 0-sentinel. LampStore will set Settings directly.

    public static AppState WithSettings(AppState state, Settings settings) =>
        Copy(state) with { Settings = CleanSettings(settings) };

    static AppState Copy(AppState s) => new()
    {
        Onboarded = s.Onboarded,
        Tasks = s.Tasks,
        ActiveTaskId = s.ActiveTaskId,
        Session = s.Session,
        Thoughts = s.Thoughts,
        Intention = s.Intention,
        IntentionDate = s.IntentionDate,
        Settings = s.Settings,
        History = s.History,
        CyclesToday = s.CyclesToday,
        CyclesDate = s.CyclesDate,
    };

    static TaskItem Clone(TaskItem t) => new()
    {
        Id = t.Id,
        Title = t.Title,
        Done = t.Done,
        CreatedAt = t.CreatedAt,
        CompletedAt = t.CompletedAt,
        FocusedMs = t.FocusedMs,
    };

    static Thought Clone(Thought t) => new() { Id = t.Id, Text = t.Text, CreatedAt = t.CreatedAt };

    static Session Clone(Session s) => new()
    {
        Id = s.Id,
        TaskId = s.TaskId,
        Kind = s.Kind,
        DurationMs = s.DurationMs,
        StartedAt = s.StartedAt,
        ElapsedMs = s.ElapsedMs,
        RunningSince = s.RunningSince,
        Status = s.Status,
    };

    static DayStat Clone(DayStat d) => new()
    {
        Date = d.Date,
        FocusedMs = d.FocusedMs,
        Sessions = d.Sessions,
        CompletedTasks = d.CompletedTasks,
    };

    static Settings Clone(Settings s) => new()
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
}

public enum ActionKind
{
    CompleteOnboarding,
    AddTask,
    RemoveTask,
    ToggleDone,
    CompleteCurrent,
    SelectTask,
    SetIntention,
    StartFocus,
    StartBreak,
    Pause,
    Resume,
    Stop,
    SessionFinished,
    AckDone,
    AddThought,
    PromoteThought,
    DismissThought,
    PatchSettings,
}
