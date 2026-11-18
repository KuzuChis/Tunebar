namespace Tunebar.Core;

/// <summary>Where a media source comes from: a browser tab (extension) or Windows SMTC.</summary>
public enum SourceKind { ExtensionTab, Smtc }

/// <summary>Repeat behaviour of a player.</summary>
public enum LoopMode { Off, Playlist, One }

/// <summary>One entry in a playlist.</summary>
public sealed record PlaylistItem(string Title, string? Artist);

/// <summary>A playlist and which item (zero-based) is playing now.</summary>
public sealed record Playlist(int Index, IReadOnlyList<PlaylistItem> Items);

/// <summary>A snapshot of one thing that is playing (or paused) that the tray can control.</summary>
public sealed record MediaSource(
    string Id,              // "tab:<tabId>" atau "smtc:<aumid>"
    SourceKind Kind,
    string AppName,         // "Opera", "Spotify", …
    string OriginLabel,     // "Opera · tab YouTube", "Opera · YouTube Music", "Spotify"
    string Title, string? Artist,
    string? CoverUrl, byte[]? CoverBytes,
    bool IsPlaying,
    double? PositionSec, double? DurationSec,
    bool CanNext, bool CanPrev,
    double? Volume, double MaxVolume,   // Volume null = slider disembunyikan
    LoopMode Loop, IReadOnlyList<LoopMode> LoopModes,
    Playlist? Playlist)
{
    /// <summary>Seeking is only possible when the duration is known.</summary>
    public bool CanSeek => DurationSec is not null;
}
