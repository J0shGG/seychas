using System.IO;
using System.Text;
using Lamp.Core;

namespace Lamp.Services;

public static class ElectronImport
{
    public static string FlagPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Lamp",
        "electron-import.done");

    public static AppState ApplyOnce(AppState lamp)
    {
        if (File.Exists(FlagPath)) return lamp;
        var electron = ReadBest();
        var merged = Merge(lamp, electron);
        if (electron is not null) StorageService.Save(merged);
        WriteFlag();
        return merged;
    }

    public static AppState ImportNow(AppState? lamp = null)
    {
        lamp ??= File.Exists(StorageService.FilePath) ? StorageService.LoadWithoutImport() : new AppState();
        var electron = ReadBest();
        var merged = Merge(lamp, electron);
        StorageService.Save(merged);
        WriteFlag();
        return merged;
    }

    public static AppState? ReadBest()
    {
        AppState? best = null;
        var bestScore = -1;
        foreach (var dir in CandidateDirs())
        {
            var level = Path.Combine(dir, "Local Storage", "leveldb");
            if (!Directory.Exists(level)) continue;
            string[] files;
            try { files = Directory.GetFiles(level); }
            catch { continue; }
            foreach (var file in files)
            {
                var name = Path.GetFileName(file);
                if (name is "LOCK" or "CURRENT" or "LOG" || name.EndsWith(".old", StringComparison.OrdinalIgnoreCase))
                    continue;
                byte[] bytes;
                try { bytes = File.ReadAllBytes(file); }
                catch { continue; }
                foreach (var state in ExtractStates(bytes))
                {
                    var score = Score(state);
                    if (score >= bestScore)
                    {
                        best = state;
                        bestScore = score;
                    }
                }
            }
        }
        return best;
    }

    static AppState Merge(AppState lamp, AppState? electron)
    {
        lamp = StateReducer.Hydrate(lamp);
        if (electron is null) return lamp;
        electron = StateReducer.Hydrate(electron);
        if (!lamp.Onboarded && !lamp.Tasks.Any() && lamp.History.All(d => d.FocusedMs == 0))
            return electron;

        var tasks = lamp.Tasks.Select(CloneTask).ToList();
        var ids = tasks.Select(t => t.Id).ToHashSet();
        var titles = tasks.Select(t => t.Title).ToHashSet(StringComparer.Ordinal);
        foreach (var task in electron.Tasks)
        {
            if (ids.Contains(task.Id) || titles.Contains(task.Title)) continue;
            tasks.Add(CloneTask(task));
            ids.Add(task.Id);
            titles.Add(task.Title);
        }

        var thoughts = lamp.Thoughts.Select(t => new Thought { Id = t.Id, Text = t.Text, CreatedAt = t.CreatedAt }).ToList();
        var thoughtIds = thoughts.Select(t => t.Id).ToHashSet();
        foreach (var thought in electron.Thoughts)
        {
            if (thoughtIds.Contains(thought.Id)) continue;
            thoughts.Add(new Thought { Id = thought.Id, Text = thought.Text, CreatedAt = thought.CreatedAt });
        }

        var history = lamp.History.Select(CloneDay).ToList();
        foreach (var day in electron.History)
        {
            var i = history.FindIndex(d => d.Date == day.Date);
            if (i < 0) history.Add(CloneDay(day));
            else
            {
                history[i].FocusedMs = Math.Max(history[i].FocusedMs, day.FocusedMs);
                history[i].Sessions = Math.Max(history[i].Sessions, day.Sessions);
                history[i].CompletedTasks = Math.Max(history[i].CompletedTasks, day.CompletedTasks);
            }
        }

        return lamp with
        {
            Onboarded = lamp.Onboarded || electron.Onboarded,
            Tasks = tasks,
            Thoughts = thoughts,
            History = history,
            ActiveTaskId = lamp.ActiveTaskId ?? electron.ActiveTaskId,
            Intention = string.IsNullOrWhiteSpace(lamp.Intention) ? electron.Intention : lamp.Intention,
            CyclesToday = Math.Max(lamp.CyclesToday, electron.CyclesToday),
        };
    }

    static int Score(AppState state) =>
        (state.Onboarded ? 10 : 0)
        + state.Tasks.Count * 4
        + state.History.Count(d => d.FocusedMs > 0) * 3
        + state.Thoughts.Count
        + (string.IsNullOrWhiteSpace(state.Intention) ? 0 : 2);

    static IEnumerable<string> CandidateDirs()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] names = ["Seychas", "seychas", "Electron"];
        foreach (var name in names)
        {
            yield return Path.Combine(roaming, name);
            yield return Path.Combine(local, name);
        }
    }

    public static IEnumerable<AppState> ExtractStates(byte[] bytes)
    {
        var seen = new HashSet<string>();
        foreach (var text in new[] { Encoding.UTF8.GetString(bytes), Encoding.Unicode.GetString(bytes) })
        {
            var from = 0;
            while (from < text.Length)
            {
                var hit = text.IndexOf("\"onboarded\"", from, StringComparison.Ordinal);
                if (hit < 0) break;
                var start = text.LastIndexOf('{', hit);
                from = hit + 11;
                if (start < 0) continue;
                var json = SliceObject(text, start);
                if (json is null || !json.Contains("\"tasks\"", StringComparison.Ordinal)) continue;
                if (!seen.Add(json)) continue;
                AppState state;
                try { state = StorageService.Parse(json); }
                catch { continue; }
                yield return state;
            }
        }
    }

    static string? SliceObject(string text, int start)
    {
        var depth = 0;
        var inString = false;
        var escape = false;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (escape) escape = false;
                else if (c == '\\') escape = true;
                else if (c == '"') inString = false;
                continue;
            }
            if (c == '"') inString = true;
            else if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0) return text[start..(i + 1)];
            }
        }
        return null;
    }

    static void WriteFlag()
    {
        try
        {
            var dir = Path.GetDirectoryName(FlagPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(FlagPath, DateTime.UtcNow.ToString("o"));
        }
        catch
        {
            /* next launch can try again */
        }
    }

    static TaskItem CloneTask(TaskItem t) => new()
    {
        Id = t.Id,
        Title = t.Title,
        Done = t.Done,
        CreatedAt = t.CreatedAt,
        CompletedAt = t.CompletedAt,
        FocusedMs = t.FocusedMs,
    };

    static DayStat CloneDay(DayStat d) => new()
    {
        Date = d.Date,
        FocusedMs = d.FocusedMs,
        Sessions = d.Sessions,
        CompletedTasks = d.CompletedTasks,
    };
}
