using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace WinlyStart.Core;

public enum ThemeMode { Auto, Light, Dark }
public enum TileSize { Small, Medium, Wide, Large }

/// <summary>User options. Persisted to %LocalAppData%\WinlyStart\settings.json.</summary>
public sealed class Settings
{
    public bool ReplaceWinKey { get; set; } = true;
    public bool ReplaceStartButton { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public ThemeMode Theme { get; set; } = ThemeMode.Auto;
    /// <summary>Tile units per group row (each medium tile is 2 units). 6 = three medium tiles wide.
    /// Adjustable 4–12 by dragging the resize grip.</summary>
    public int TileColumns { get; set; } = 6;
    public int MenuHeight { get; set; } = 640;
    public bool ShowRecentlyAdded { get; set; } = true;
    public bool ShowMostUsed { get; set; } = true;
    /// <summary>Align the menu under the Windows 11 Start button instead of the screen corner.</summary>
    public bool OpenAtStartButton { get; set; }
    public bool TrimMemoryWhenHidden { get; set; } = true;
}

public sealed class Tile
{
    public string AppId { get; set; } = string.Empty;
    public TileSize Size { get; set; } = TileSize.Medium;
    public int Col { get; set; }
    public int Row { get; set; }
}

public sealed class TileGroup
{
    public string Name { get; set; } = string.Empty;
    public List<Tile> Tiles { get; set; } = new();
}

public sealed class TileLayout
{
    public List<TileGroup> Groups { get; set; } = new();
}

/// <summary>Launch counters ("Most used") and first-seen dates ("Recently added" for packaged apps).</summary>
public sealed class UsageData
{
    public Dictionary<string, int> Launches { get; set; } = new();
    public Dictionary<string, DateTime> FirstSeen { get; set; } = new();
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(Settings))]
[JsonSerializable(typeof(TileLayout))]
[JsonSerializable(typeof(UsageData))]
internal partial class JsonCtx : JsonSerializerContext { }

/// <summary>Tiny JSON file store. Reflection-free (source generated) to keep the working set small.</summary>
public static class Store
{
    public static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinlyStart");

    public static T Load<T>(string file, JsonTypeInfo<T> type) where T : new()
    {
        try
        {
            var path = Path.Combine(Dir, file);
            if (File.Exists(path))
                return JsonSerializer.Deserialize(File.ReadAllText(path), type) ?? new T();
        }
        catch { /* corrupt or unreadable → start fresh, never crash the shell replacement */ }
        return new T();
    }

    public static void Save<T>(string file, T value, JsonTypeInfo<T> type)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var path = Path.Combine(Dir, file);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(value, type));
            File.Move(path + ".tmp", path, overwrite: true);
        }
        catch { /* disk full / locked — keep running with in-memory state */ }
    }
}
