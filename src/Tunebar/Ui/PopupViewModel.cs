using System.ComponentModel;
using Tunebar.Core;

namespace Tunebar.Ui;

/// <summary>Segoe Fluent Icons characters used by the popup.</summary>
public static class Glyphs
{
    public const string Previous = "";
    public const string Next = "";
    public const string Play = "";
    public const string Pause = "";
    public const string RepeatAll = "";
    public const string RepeatOne = "";
    public const string Lock = "";
    public const string Unlock = "";
    public const string Volume = "";
    public const string ChevronDown = "";
    public const string ChevronUp = "";
    public const string Music = "";
}

/// <summary>One line of the playlist list.</summary>
public sealed record PlaylistRow(int Index, string Title, string? Artist, bool IsCurrent);

/// <summary>
/// One line of the "other sources" list. It is a long-lived object so the list keeps the same
/// buttons while the data refreshes every second (a rebuilt button would swallow a click).
/// </summary>
public sealed class OtherSourceRow : INotifyPropertyChanged
{
    private MediaSource _source;

    public OtherSourceRow(MediaSource source) => _source = source;

    public event PropertyChangedEventHandler? PropertyChanged;

    public MediaSource Source
    {
        get => _source;
        set
        {
            _source = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Source)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PlayGlyph)));
        }
    }

    public string Id => _source.Id;

    /// <summary>Pause icon while playing, play icon while paused.</summary>
    public string PlayGlyph => _source.IsPlaying ? Glyphs.Pause : Glyphs.Play;
}

/// <summary>
/// Everything the popup shows and does, without any WPF types, so it can be tested.
/// It listens to <see cref="SourceRegistry.Changed"/> and tells the view what to re-read.
/// </summary>
public sealed class PopupViewModel : INotifyPropertyChanged
{
    private readonly SourceRegistry _registry;
    private readonly ISourceCommands _commands;
    private readonly Action<Action> _dispatch;

    // The registry recomputes its ordering on every read, so we keep a copy per change.
    private MediaSource? _current;
    private IReadOnlyList<MediaSource> _others = [];
    private IReadOnlyList<PlaylistRow> _playlistRows = [];
    private IReadOnlyList<OtherSourceRow> _otherRows = [];
    private bool _isOthersExpanded;

    /// <param name="dispatch">Runs an action on the UI thread. Tests pass <c>a => a()</c>.</param>
    public PopupViewModel(SourceRegistry registry, ISourceCommands commands, Action<Action> dispatch)
    {
        _registry = registry;
        _commands = commands;
        _dispatch = dispatch;

        ReadRegistry();
        _registry.Changed += (_, _) => _dispatch(OnRegistryChanged);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // ---- What the popup shows ----

    public MediaSource? Current => _current;
    public bool HasCurrent => _current is not null;
    public bool IsLocked => _registry.LockedId is not null && _registry.LockedId == _current?.Id;
    public string LockGlyph => IsLocked ? Glyphs.Lock : Glyphs.Unlock;
    public bool IsPlaying => _current?.IsPlaying ?? false;
    public string PlayPauseGlyph => IsPlaying ? Glyphs.Pause : Glyphs.Play;

    public string PositionText => Format.Time(_current?.PositionSec);
    public string DurationText => Format.Time(_current?.DurationSec);

    public bool ShowVolume => _current?.Volume is not null;
    public bool ShowLoop => _current is { LoopModes.Count: > 0 };
    public bool ShowPlaylist => _current?.Playlist is { Items.Count: > 0 };

    public string? PlaylistHeader => _current?.Playlist is { } playlist ? Format.PlaylistHeader(playlist) : null;

    /// <summary>Playlist lines. The same list is returned until the playlist or its active song changes.</summary>
    public IReadOnlyList<PlaylistRow> PlaylistRows => _playlistRows;

    public string OthersText => Format.OthersLabel(_others.Count, _others.Count(o => o.IsPlaying));
    public IReadOnlyList<MediaSource> Others => _others;

    /// <summary>The other sources as long-lived rows (what the list in the popup binds to).</summary>
    public IReadOnlyList<OtherSourceRow> OtherRows => _otherRows;

    public bool HasOthers => _others.Count > 0;

    public bool IsOthersExpanded
    {
        get => _isOthersExpanded;
        set
        {
            if (_isOthersExpanded == value) return;
            _isOthersExpanded = value;
            Raise(nameof(IsOthersExpanded));
            Raise(nameof(OthersChevronGlyph));
        }
    }

    public string OthersChevronGlyph => _isOthersExpanded ? Glyphs.ChevronUp : Glyphs.ChevronDown;

    /// <summary>Repeat-one has its own icon; the other modes use repeat-all.</summary>
    public string LoopGlyph => _current?.Loop == LoopMode.One ? Glyphs.RepeatOne : Glyphs.RepeatAll;

    /// <summary>True when looping is on (the icon gets the accent color).</summary>
    public bool IsLoopActive => _current is { Loop: not LoopMode.Off };

    // ---- What the user can do ----

    public Task TogglePlayAsync() => _current is { } s ? _commands.TogglePlayAsync(s) : Task.CompletedTask;

    public Task NextAsync() => _current is { } s ? _commands.NextAsync(s) : Task.CompletedTask;

    public Task PrevAsync() => _current is { } s ? _commands.PrevAsync(s) : Task.CompletedTask;

    public Task CycleLoopAsync() =>
        _current is { } s
            ? _commands.SetLoopAsync(s, LoopCycle.Next(s.Loop, s.LoopModes))
            : Task.CompletedTask;

    public void ToggleLock()
    {
        if (_current is null) return;
        if (IsLocked) _registry.Unlock();
        else _registry.Lock(_current.Id);
    }

    public Task SetVolumeAsync(double v) => _current is { } s ? _commands.SetVolumeAsync(s, v) : Task.CompletedTask;

    public Task SeekAsync(double sec) => _current is { } s ? _commands.SeekAsync(s, sec) : Task.CompletedTask;

    public Task JumpAsync(int index) => _current is { } s ? _commands.JumpAsync(s, index) : Task.CompletedTask;

    /// <summary>Makes another source the shown one (until something else starts playing).</summary>
    public void SelectSource(string id) => _registry.Select(id);

    /// <summary>Play/pause a source from the "other sources" list.</summary>
    public Task TogglePlayOtherAsync(string id)
    {
        var source = _others.FirstOrDefault(o => o.Id == id);
        return source is null ? Task.CompletedTask : _commands.TogglePlayAsync(source);
    }

    /// <summary>Locks a source from the "other sources" list; it becomes the shown source.</summary>
    public void LockSource(string id) => _registry.Lock(id);

    public void ToggleOthers() => IsOthersExpanded = !IsOthersExpanded;

    // ---- Keeping up with the registry ----

    private void OnRegistryChanged()
    {
        var (playlistChanged, othersChanged) = ReadRegistry();
        RaiseAll(playlistChanged, othersChanged);
    }

    /// <summary>
    /// Copies Current/Others from the registry. Tells whether the playlist rows and
    /// the other-source rows were replaced (so the view must rebuild those lists).
    /// </summary>
    private (bool PlaylistChanged, bool OthersChanged) ReadRegistry()
    {
        _current = _registry.Current;
        _others = _registry.Others;
        var othersChanged = SyncOtherRows();
        var playlistChanged = SyncPlaylistRows();
        return (playlistChanged, othersChanged);
    }

    private bool SyncPlaylistRows()
    {
        var playlist = _current?.Playlist;
        if (playlist is null)
        {
            var hadRows = _playlistRows.Count > 0;
            _playlistRows = [];
            return hadRows;
        }

        // Keep the old list when nothing visible changed, so the ListBox does not jump back to the top.
        var same = _playlistRows.Count == playlist.Items.Count
            && _playlistRows.Select((row, i) => row.Title == playlist.Items[i].Title
                                                && row.Artist == playlist.Items[i].Artist
                                                && row.IsCurrent == (i == playlist.Index)).All(ok => ok);
        if (same) return false;

        _playlistRows = playlist.Items
            .Select((item, i) => new PlaylistRow(i, item.Title, item.Artist, i == playlist.Index))
            .ToList();
        return true;
    }

    private bool SyncOtherRows()
    {
        var sameIds = _otherRows.Select(r => r.Id).SequenceEqual(_others.Select(o => o.Id));
        if (sameIds)
        {
            // Same sources in the same order: just hand each row its fresh data.
            for (var i = 0; i < _others.Count; i++) _otherRows[i].Source = _others[i];
            return false;
        }

        _otherRows = _others.Select(o => new OtherSourceRow(o)).ToList();
        return true;
    }

    private void RaiseAll(bool playlistChanged, bool othersChanged)
    {
        Raise(nameof(Current));
        Raise(nameof(HasCurrent));
        Raise(nameof(IsLocked));
        Raise(nameof(LockGlyph));
        Raise(nameof(IsPlaying));
        Raise(nameof(PlayPauseGlyph));
        Raise(nameof(PositionText));
        Raise(nameof(DurationText));
        Raise(nameof(ShowVolume));
        Raise(nameof(ShowLoop));
        Raise(nameof(ShowPlaylist));
        Raise(nameof(PlaylistHeader));
        if (playlistChanged) Raise(nameof(PlaylistRows));
        Raise(nameof(OthersText));
        Raise(nameof(Others));
        Raise(nameof(HasOthers));
        if (othersChanged) Raise(nameof(OtherRows));
        Raise(nameof(LoopGlyph));
        Raise(nameof(IsLoopActive));
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
