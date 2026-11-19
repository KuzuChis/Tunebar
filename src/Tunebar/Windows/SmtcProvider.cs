﻿using System.Collections.Concurrent;
using System.IO;
using Tunebar.Core;
using SmtcManager = global::Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;
using SmtcSession = global::Windows.Media.Control.GlobalSystemMediaTransportControlsSession;
using SmtcMediaProperties = global::Windows.Media.Control.GlobalSystemMediaTransportControlsSessionMediaProperties;
using SmtcTimeline = global::Windows.Media.Control.GlobalSystemMediaTransportControlsSessionTimelineProperties;
using SmtcPlaybackStatus = global::Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus;
using SmtcRepeatMode = global::Windows.Media.MediaPlaybackAutoRepeatMode;
using MediaHandler = global::Windows.Foundation.TypedEventHandler<
    global::Windows.Media.Control.GlobalSystemMediaTransportControlsSession,
    global::Windows.Media.Control.MediaPropertiesChangedEventArgs>;
using PlaybackHandler = global::Windows.Foundation.TypedEventHandler<
    global::Windows.Media.Control.GlobalSystemMediaTransportControlsSession,
    global::Windows.Media.Control.PlaybackInfoChangedEventArgs>;
using TimelineHandler = global::Windows.Foundation.TypedEventHandler<
    global::Windows.Media.Control.GlobalSystemMediaTransportControlsSession,
    global::Windows.Media.Control.TimelinePropertiesChangedEventArgs>;

namespace Tunebar.Windows;

/// <summary>
/// Shows every Windows media session (Spotify, VLC, ...) as a <see cref="MediaSource"/>
/// and carries out commands on them. Events are raised from background threads.
/// Disposing it also disposes the <see cref="AppVolumeController"/> it was given.
/// </summary>
public sealed class SmtcProvider : ISourceCommands, IDisposable
{
    private const string IdPrefix = "smtc:";
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(1000);
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(150);
    private static readonly LoopMode[] AllLoopModes = [LoopMode.Off, LoopMode.Playlist, LoopMode.One];

    private readonly SmtcManager _manager;
    private readonly AppVolumeController _volume;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly Func<DateTimeOffset> _clock;
    // Cover state per aumid, so the thumbnail stream is not read every second.
    private readonly ConcurrentDictionary<string, CoverState> _covers = new();
    // The cover of the previous title per app, to spot Windows still serving it after a track change.
    private readonly ConcurrentDictionary<string, byte[]?> _previousCover = new();
    private static readonly TimeSpan SameCoverAcceptAfter = TimeSpan.FromSeconds(3);
    // Keeps the last good source of each app for a few seconds across track changes.
    private readonly SourceGrace _grace = new();
    // Sessions we listen to, by aumid. Only touched while holding _refreshLock (and in Dispose).
    private readonly Dictionary<string, Subscription> _subscriptions = new();
    private readonly object _debounceGate = new();
    private Timer? _debounce;
    private bool _disposed;

    private SmtcProvider(SmtcManager manager, AppVolumeController volume, Func<DateTimeOffset>? clock = null)
    {
        _manager = manager;
        _volume = volume;
        _clock = clock ?? (() => DateTimeOffset.Now);
        _debounce = new Timer(_ => RunDebouncedRefresh(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>One session and the handlers attached to it, so they can be removed again.</summary>
    private sealed record Subscription(
        SmtcSession Session, MediaHandler Media, PlaybackHandler Playback, TimelineHandler Timeline);

    /// <summary>Raised with the full list of Windows sources about once per second and when sessions change.</summary>
    public event Action<IReadOnlyList<MediaSource>>? SourcesChanged;

    /// <summary>Connects to Windows and starts the background refresh loop.</summary>
    public static async Task<SmtcProvider> CreateAsync(AppVolumeController volume)
    {
        var manager = await SmtcManager.RequestAsync();
        var provider = new SmtcProvider(manager, volume);
        manager.SessionsChanged += provider.OnSessionsChanged;
        _ = Task.Run(provider.RefreshLoopAsync);
        return provider;
    }

    // ---- Refreshing ----

    private void OnSessionsChanged(SmtcManager sender, object args) => ScheduleRefresh();

    /// <summary>Asks for a refresh in ~150 ms; calls arriving in the meantime are merged into one.</summary>
    private void ScheduleRefresh()
    {
        try
        {
            lock (_debounceGate)
            {
                if (_disposed) return;
                _debounce?.Change(DebounceDelay, Timeout.InfiniteTimeSpan);
            }
        }
        catch (Exception)
        {
            // Never throw out of an event handler.
        }
    }

    private void RunDebouncedRefresh()
    {
        try
        {
            _ = RefreshAsync();
        }
        catch (Exception)
        {
            // Never throw out of a timer callback.
        }
    }

    private async Task RefreshLoopAsync()
    {
        using var timer = new PeriodicTimer(RefreshInterval);
        try
        {
            await RefreshAsync();
            while (await timer.WaitForNextTickAsync(_stop.Token))
                await RefreshAsync();
        }
        catch (OperationCanceledException)
        {
            // Dispose was called.
        }
    }

    /// <summary>Builds the source list from the current sessions and raises <see cref="SourcesChanged"/>.</summary>
    private async Task RefreshAsync()
    {
        if (_stop.IsCancellationRequested) return;
        try
        {
            await _refreshLock.WaitAsync(_stop.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            var built = new Dictionary<string, MediaSource?>();
            var live = new HashSet<string>();
            foreach (var session in _manager.GetSessions())
            {
                string aumid;
                try
                {
                    aumid = session.SourceAppUserModelId;
                }
                catch (Exception)
                {
                    continue;
                }
                live.Add(aumid);
                Subscribe(aumid, session);
                try
                {
                    built[IdPrefix + aumid] = await BuildSourceAsync(session);
                }
                catch (Exception)
                {
                    // One broken session must not stop the others; the grace copy covers it.
                    built[IdPrefix + aumid] = null;
                }
            }
            Unsubscribe(live);
            var sources = _grace.Update(built, _clock());
            if (!_stop.IsCancellationRequested) SourcesChanged?.Invoke(sources);
        }
        catch (Exception)
        {
            // Windows may fail temporarily (or a subscriber threw); the next tick tries again.
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    // ---- Session events ----

    /// <summary>Starts listening to this session once; later calls for the same session do nothing.</summary>
    private void Subscribe(string aumid, SmtcSession session)
    {
        try
        {
            if (_subscriptions.TryGetValue(aumid, out var existing))
            {
                if (ReferenceEquals(existing.Session, session)) return;
                Detach(existing); // the app made a new session object
            }
            var sub = new Subscription(
                session,
                (_, _) =>
                {
                    if (_covers.TryGetValue(aumid, out var cover)) cover.Dirty = true;
                    ScheduleRefresh();
                },
                (_, _) => ScheduleRefresh(),
                (_, _) => ScheduleRefresh());
            session.MediaPropertiesChanged += sub.Media;
            session.PlaybackInfoChanged += sub.Playback;
            session.TimelinePropertiesChanged += sub.Timeline;
            _subscriptions[aumid] = sub;
        }
        catch (Exception)
        {
            // Without events we still have the 1 s poll.
        }
    }

    /// <summary>Stops listening to sessions that are no longer in <paramref name="live"/>.</summary>
    private void Unsubscribe(HashSet<string> live)
    {
        foreach (var aumid in _subscriptions.Keys.Where(k => !live.Contains(k)).ToList())
        {
            Detach(_subscriptions[aumid]);
            _subscriptions.Remove(aumid);
        }
    }

    private static void Detach(Subscription sub)
    {
        try
        {
            sub.Session.MediaPropertiesChanged -= sub.Media;
            sub.Session.PlaybackInfoChanged -= sub.Playback;
            sub.Session.TimelinePropertiesChanged -= sub.Timeline;
        }
        catch (Exception)
        {
            // The session may already be gone.
        }
    }

    // ---- Mapping one session to a MediaSource ----

    /// <summary>Maps one session to a source; null when it has no title yet (mid track change).</summary>
    private async Task<MediaSource?> BuildSourceAsync(SmtcSession session)
    {
        var aumid = session.SourceAppUserModelId;
        var appName = AumidMapper.AppName(aumid);
        var properties = await session.TryGetMediaPropertiesAsync();
        if (properties is null || string.IsNullOrWhiteSpace(properties.Title)) return null;
        var playback = session.GetPlaybackInfo();
        var timeline = session.GetTimelineProperties();
        var loopModes = LoopModesOf(playback.AutoRepeatMode);
        var isPlaying = playback.PlaybackStatus == SmtcPlaybackStatus.Playing;
        var duration = DurationOf(timeline);

        return new MediaSource(
            Id: IdPrefix + aumid,
            Kind: SourceKind.Smtc,
            AppName: appName,
            OriginLabel: appName,
            Title: properties.Title,
            Artist: string.IsNullOrEmpty(properties.Artist) ? null : properties.Artist,
            CoverUrl: null,
            CoverBytes: await GetCoverAsync(aumid, properties),
            IsPlaying: isPlaying,
            PositionSec: SmtcMath.CurrentPosition(
                timeline.Position, timeline.LastUpdatedTime, _clock(), isPlaying, duration),
            DurationSec: duration,
            CanNext: playback.Controls.IsNextEnabled,
            CanPrev: playback.Controls.IsPreviousEnabled,
            Volume: VolumeOf(aumid),
            MaxVolume: 1.0,
            Loop: ToLoopMode(playback.AutoRepeatMode),
            LoopModes: loopModes,
            Playlist: null);
    }

    /// <summary>The track length in seconds, or null when Windows reports 0 (unknown).</summary>
    private static double? DurationOf(SmtcTimeline timeline)
    {
        var seconds = (timeline.EndTime - timeline.StartTime).TotalSeconds;
        return seconds > 0 ? seconds : null;
    }

    /// <summary>The app volume (0..1). Null for browsers (volume is per tab) and apps we cannot find.</summary>
    private double? VolumeOf(string aumid)
    {
        if (AumidMapper.IsBrowser(aumid)) return null;
        var processName = AumidMapper.ProcessName(aumid);
        return processName is null ? null : _volume.GetVolume(processName);
    }

    /// <summary>No repeat info from the app means it has no repeat button: empty list.</summary>
    private static IReadOnlyList<LoopMode> LoopModesOf(SmtcRepeatMode? mode) =>
        mode is null ? [] : AllLoopModes;

    private static LoopMode ToLoopMode(SmtcRepeatMode? mode) => mode switch
    {
        SmtcRepeatMode.List => LoopMode.Playlist,
        SmtcRepeatMode.Track => LoopMode.One,
        _ => LoopMode.Off,
    };

    private static SmtcRepeatMode ToRepeatMode(LoopMode mode) => mode switch
    {
        LoopMode.Playlist => SmtcRepeatMode.List,
        LoopMode.One => SmtcRepeatMode.Track,
        _ => SmtcRepeatMode.None,
    };

    // ---- Cover art ----

    /// <summary>
    /// Returns the cover for this app's current title. The thumbnail is read again only when
    /// <see cref="CoverRefreshPolicy"/> says so; a new title never shows the previous track's cover.
    /// </summary>
    private async Task<byte[]?> GetCoverAsync(string aumid, SmtcMediaProperties properties)
    {
        var now = _clock();
        var title = properties.Title;
        _covers.TryGetValue(aumid, out var state);
        if (!CoverRefreshPolicy.ShouldRead(state, title, now)) return state!.Bytes;

        var titleChanged = state is null || state.Title != title;
        state ??= new CoverState();
        state.Dirty = false; // cleared before reading so a change during the read is not lost
        var bytes = await ReadThumbnailAsync(properties);
        if (titleChanged) _previousCover[aumid] = state.Bytes;
        // Windows often still hands out the previous track's picture for a moment after the title
        // changed. Treat that as "not ready yet" (the retry policy reads again) instead of showing
        // a wrong cover. Still the same after 3 s means the tracks really share it (same album).
        var sinceTitle = titleChanged ? TimeSpan.Zero : now - state.TitleSince;
        if (bytes is not null && sinceTitle < SameCoverAcceptAfter
            && _previousCover.TryGetValue(aumid, out var previous) && previous is not null
            && bytes.AsSpan().SequenceEqual(previous))
            bytes = null;
        if (titleChanged)
        {
            state.Title = title;
            state.TitleSince = now;
            state.Bytes = bytes;
        }
        else if (bytes is not null)
        {
            state.Bytes = bytes;
        }
        state.ReadAt = now;
        _covers[aumid] = state;
        return state.Bytes;
    }

    private static async Task<byte[]?> ReadThumbnailAsync(SmtcMediaProperties properties)
    {
        try
        {
            if (properties.Thumbnail is null) return null;
            using var winRtStream = await properties.Thumbnail.OpenReadAsync();
            using var stream = winRtStream.AsStreamForRead();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            return memory.Length == 0 ? null : memory.ToArray();
        }
        catch (Exception)
        {
            return null; // no cover is better than no source
        }
    }

    // ---- Commands ----

    /// <summary>Finds the live Windows session for this source; null when it is gone.</summary>
    private SmtcSession? FindSession(MediaSource s)
    {
        try
        {
            return _manager.GetSessions().FirstOrDefault(x => IdPrefix + x.SourceAppUserModelId == s.Id);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Runs one command on the session of <paramref name="s"/>. Does nothing if it is gone; swallows errors.</summary>
    private async Task RunAsync(MediaSource s, Func<SmtcSession, Task> command)
    {
        var session = FindSession(s);
        if (session is null) return;
        try
        {
            await command(session);
        }
        catch (Exception)
        {
            // The app may have closed while we were sending; nothing to do.
        }
    }

    /// <inheritdoc />
    public Task TogglePlayAsync(MediaSource s) => RunAsync(s, x => x.TryTogglePlayPauseAsync().AsTask());
    /// <inheritdoc />
    public Task NextAsync(MediaSource s) => RunAsync(s, x => x.TrySkipNextAsync().AsTask());
    /// <inheritdoc />
    public Task PrevAsync(MediaSource s) => RunAsync(s, x => x.TrySkipPreviousAsync().AsTask());

    /// <inheritdoc />
    public Task SeekAsync(MediaSource s, double seconds) =>
        RunAsync(s, x => x.TryChangePlaybackPositionAsync(TimeSpan.FromSeconds(seconds).Ticks).AsTask());

    /// <inheritdoc />
    public Task SetLoopAsync(MediaSource s, LoopMode mode) =>
        RunAsync(s, x => x.TryChangeAutoRepeatModeAsync(ToRepeatMode(mode)).AsTask());

    /// <inheritdoc />
    public Task SetVolumeAsync(MediaSource s, double volume)
    {
        var aumid = s.Id.StartsWith(IdPrefix, StringComparison.Ordinal) ? s.Id[IdPrefix.Length..] : s.Id;
        if (AumidMapper.IsBrowser(aumid)) return Task.CompletedTask;
        var processName = AumidMapper.ProcessName(aumid);
        if (processName is null) return Task.CompletedTask;
        // The Core Audio call is blocking COM work: keep it off the UI thread (slider drags).
        return Task.Run(() => _volume.SetVolume(processName, Math.Clamp(volume, 0.0, 1.0)));
    }

    /// <summary>Windows media sessions have no playlist, so this does nothing.</summary>
    public Task JumpAsync(MediaSource s, int index) => Task.CompletedTask;

    // ---- Cleanup ----

    /// <summary>Stops the refresh loop and unsubscribes from Windows. Raises no more events.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        lock (_debounceGate)
        {
            _disposed = true;
            _debounce?.Dispose();
            _debounce = null;
        }
        _stop.Cancel();
        _manager.SessionsChanged -= OnSessionsChanged;
        foreach (var sub in _subscriptions.Values.ToList()) Detach(sub);
        _subscriptions.Clear();
        _volume.Dispose();
    }
}
