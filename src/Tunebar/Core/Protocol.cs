using System.Text.Json;

namespace Tunebar.Core;

/// <summary>Reads and writes the JSON messages exchanged with the browser extension.</summary>
public static class Protocol
{
    private const string TabIdPrefix = "tab:";

    /// <summary>
    /// Turns a <c>{"type":"tabs",...}</c> message into media sources.
    /// Returns null when the text is not valid JSON or is not a tabs message.
    /// Tab entries without <c>tabId</c> or <c>title</c> are skipped.
    /// </summary>
    public static IReadOnlyList<MediaSource>? ParseTabs(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (GetString(root, "type") != "tabs") return null;
            if (!root.TryGetProperty("tabs", out var tabsElement) || tabsElement.ValueKind != JsonValueKind.Array)
                return null;

            var result = new List<MediaSource>();
            foreach (var tab in tabsElement.EnumerateArray())
            {
                var source = ParseTab(tab);
                if (source is not null) result.Add(source);
            }
            return result;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Builds the JSON text of a command for one tab.</summary>
    /// <param name="value">A number, a <see cref="LoopMode"/>, or null.</param>
    public static string BuildCommand(int tabId, string action, object? value)
    {
        object? jsonValue = value is LoopMode mode ? LoopToString(mode) : value;
        var message = new Dictionary<string, object?>
        {
            ["type"] = "command",
            ["tabId"] = tabId,
            ["action"] = action,
            ["value"] = jsonValue,
        };
        return JsonSerializer.Serialize(message);
    }

    /// <summary>Gets the browser tab number from an id like <c>"tab:12"</c>.</summary>
    /// <exception cref="ArgumentException">The source is not a tab source.</exception>
    public static int TabIdOf(MediaSource s)
    {
        if (s.Id.StartsWith(TabIdPrefix, StringComparison.Ordinal)
            && int.TryParse(s.Id.AsSpan(TabIdPrefix.Length), out var tabId))
            return tabId;
        throw new ArgumentException($"Not a tab source: '{s.Id}'", nameof(s));
    }

    private static MediaSource? ParseTab(JsonElement tab)
    {
        if (tab.ValueKind != JsonValueKind.Object) return null;
        if (!tab.TryGetProperty("tabId", out var idElement) || !idElement.TryGetInt32(out var tabId)) return null;
        var title = GetString(tab, "title");
        if (title is null) return null;

        var originLabel = GetString(tab, "site") == "ytmusic" ? "Opera · YouTube Music" : "Opera · tab YouTube";

        return new MediaSource(
            Id: TabIdPrefix + tabId,
            Kind: SourceKind.ExtensionTab,
            AppName: "Opera",
            OriginLabel: originLabel,
            Title: title,
            Artist: GetString(tab, "artist"),
            CoverUrl: GetString(tab, "coverUrl"),
            CoverBytes: null,
            IsPlaying: GetBool(tab, "isPlaying"),
            PositionSec: GetNumber(tab, "position"),
            DurationSec: GetNumber(tab, "duration"),
            CanNext: GetBool(tab, "canNext"),
            CanPrev: GetBool(tab, "canPrev"),
            Volume: GetNumber(tab, "volume"),
            MaxVolume: 1.5,
            Loop: LoopFromString(GetString(tab, "loop")) ?? LoopMode.Off,
            LoopModes: ParseLoopModes(tab),
            Playlist: ParsePlaylist(tab));
    }

    private static IReadOnlyList<LoopMode> ParseLoopModes(JsonElement tab)
    {
        var modes = new List<LoopMode>();
        if (tab.TryGetProperty("loopModes", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in array.EnumerateArray())
            {
                var mode = item.ValueKind == JsonValueKind.String ? LoopFromString(item.GetString()) : null;
                if (mode is not null) modes.Add(mode.Value);
            }
        }
        return modes;
    }

    private static Playlist? ParsePlaylist(JsonElement tab)
    {
        if (!tab.TryGetProperty("playlist", out var p) || p.ValueKind != JsonValueKind.Object) return null;
        var index = p.TryGetProperty("index", out var i) && i.TryGetInt32(out var n) ? n : 0;

        var items = new List<PlaylistItem>();
        if (p.TryGetProperty("items", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object) continue;
                items.Add(new PlaylistItem(GetString(item, "title") ?? "", GetString(item, "artist")));
            }
        }
        return new Playlist(index, items);
    }

    /// <summary>Reads a string property; null when missing or not a string.</summary>
    private static string? GetString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Reads a boolean property; false when missing or not a boolean.</summary>
    private static bool GetBool(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>Reads a finite number property; null when missing, JSON null, or not finite.</summary>
    private static double? GetNumber(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Number) return null;
        return v.TryGetDouble(out var d) && double.IsFinite(d) ? d : null;
    }

    private static LoopMode? LoopFromString(string? text) => text switch
    {
        "off" => LoopMode.Off,
        "playlist" => LoopMode.Playlist,
        "one" => LoopMode.One,
        _ => null,
    };

    private static string LoopToString(LoopMode mode) => mode switch
    {
        LoopMode.Playlist => "playlist",
        LoopMode.One => "one",
        _ => "off",
    };
}
