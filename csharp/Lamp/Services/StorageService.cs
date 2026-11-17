using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lamp.Core;

namespace Lamp.Services;

public static class StorageService
{
    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Lamp",
        "state.json");

    public static AppState Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppState();
            var raw = File.ReadAllText(FilePath);
            var state = JsonSerializer.Deserialize<AppState>(raw, Json);
            return state is null ? new AppState() : StateReducer.Hydrate(state);
        }
        catch
        {
            return new AppState();
        }
    }

    public static void Save(AppState state)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(state, Json));
        }
        catch
        {
            /* keep running if the disk is busy */
        }
    }
}
