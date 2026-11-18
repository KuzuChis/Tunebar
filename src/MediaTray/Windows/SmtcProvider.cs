using System.IO;
using MediaTray.Core;
using SmtcManager = global::Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;
using SmtcSession = global::Windows.Media.Control.GlobalSystemMediaTransportControlsSession;
using SmtcMediaProperties = global::Windows.Media.Control.GlobalSystemMediaTransportControlsSessionMediaProperties;
using SmtcTimeline = global::Windows.Media.Control.GlobalSystemMediaTransportControlsSessionTimelineProperties;
using SmtcPlaybackStatus = global::Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus;
using SmtcRepeatMode = global::Windows.Media.MediaPlaybackAutoRepeatMode;

namespace MediaTray.Windows;

/// <summary>
/// Shows every Windows media session (Spotify, VLC, ...) as a <see cref="MediaSource"/>
/// and carries out commands on them. Events are raised from background threads.
/// Disposing it also disposes the <see cref="AppVolumeController"/> it was given.
/// </summary>
public sealed class SmtcProvider : ISourceCommands, IDisposable
{
    private const string IdPrefix = "smtc:";
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(1000);
    private static readonly LoopMode[] AllLoopModes = [LoopMode.Off, LoopMode.Playlist, LoopMode.One];

    private readonly SmtcManager _manager;
    private readonly AppVolumeController _volume;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    // Cover bytes per (aumid, title), so the thumbnail stream is not read every second.
    private readonly Dictionary<(string Aumid, string Title), byte[]?> _coverCache = new();
    private bool _disposed;

    private SmtcProvider(SmtcManager manager, AppVolumeController volume)
    {
        _manager = manager;
        _volume = volume;
    }

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

    private void OnSessionsChanged(SmtcManager sender, object args) => _ = RefreshAsync();

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
            var sources = new List<MediaSource>();
            foreach (var session in _manager.GetSessions())
            {
                try
                {
                    sources.Add(await BuildSourceAsync(session));
                }
                catch (Exception)
                {
                    // One broken session is skipped; it must not stop the others.
                }
            }
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

    // ---- Mapping one session to a MediaSource ----

    private async Task<MediaSource> BuildSourceAsync(SmtcSession session)
    {
        var aumid = session.SourceAppUserModelId;
        var appName = AumidMapper.AppName(aumid);
        var properties = await session.TryGetMediaPropertiesAsync();
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
                timeline.Position, timeline.LastUpdatedTime, DateTimeOffset.Now, isPlaying, duration),
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

    /// <summary>Returns the cached cover for this (app, title), reading the thumbnail only the first time.</summary>
    private async Task<byte[]?> GetCoverAsync(string aumid, SmtcMediaProperties properties)
    {
        var key = (aumid, properties.Title);
        if (_coverCache.TryGetValue(key, out var cached)) return cached;

        var bytes = await ReadThumbnailAsync(properties);
        // Keep only the newest cover per app so the cache cannot grow without limit.
        foreach (var old in _coverCache.Keys.Where(k => k.Aumid == aumid).ToList())
            _coverCache.Remove(old);
        _coverCache[key] = bytes;
        return bytes;
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
        _disposed = true;
        _stop.Cancel();
        _manager.SessionsChanged -= OnSessionsChanged;
        _volume.Dispose();
    }
}
