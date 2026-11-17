using System.Text.Json.Serialization;

namespace Lamp.Core;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AmbientKind
{
    off,
    rain,
    fire,
    cafe,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum IslandEdge
{
    top,
    left,
    right,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ThemeId
{
    lamp,
    light,
    dark,
    ink,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SessionKind
{
    focus,
    @break,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SessionStatus
{
    running,
    paused,
    done,
}

public sealed class TaskItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public bool Done { get; set; }
    public long CreatedAt { get; set; }
    public long? CompletedAt { get; set; }
    public long FocusedMs { get; set; }
}

public sealed class Thought
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    public long CreatedAt { get; set; }
}

public sealed class Session
{
    public string Id { get; set; } = "";
    public string? TaskId { get; set; }
    public SessionKind Kind { get; set; }
    public long DurationMs { get; set; }
    public long StartedAt { get; set; }
    public long ElapsedMs { get; set; }
    public long? RunningSince { get; set; }
    public SessionStatus Status { get; set; }
}

public sealed class DayStat
{
    public string Date { get; set; } = "";
    public long FocusedMs { get; set; }
    public int Sessions { get; set; }
    public int CompletedTasks { get; set; }
}

public sealed class Settings
{
    public int FocusMin { get; set; } = 25;
    public int ShortBreakMin { get; set; } = 5;
    public int LongBreakMin { get; set; } = 15;
    public int CyclesUntilLong { get; set; } = 4;
    public bool Chime { get; set; } = true;
    public AmbientKind Ambient { get; set; } = AmbientKind.off;
    public double Volume { get; set; } = 1;
    public bool Notify { get; set; }
    public IslandEdge IslandEdge { get; set; } = IslandEdge.top;
    public ThemeId Theme { get; set; } = ThemeId.lamp;
}

public sealed record AppState
{
    public bool Onboarded { get; set; }
    public List<TaskItem> Tasks { get; set; } = [];
    public string? ActiveTaskId { get; set; }
    public Session? Session { get; set; }
    public List<Thought> Thoughts { get; set; } = [];
    public string Intention { get; set; } = "";
    public string IntentionDate { get; set; } = Format.TodayKey();
    public Settings Settings { get; set; } = new();
    public List<DayStat> History { get; set; } = [];
    public int CyclesToday { get; set; }
    public string CyclesDate { get; set; } = Format.TodayKey();
}
