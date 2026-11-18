namespace Tunebar.Core;

/// <summary>Everything the tray can ask a media source to do.</summary>
public interface ISourceCommands
{
    /// <summary>Play if paused, pause if playing.</summary>
    Task TogglePlayAsync(MediaSource s);
    /// <summary>Skip to the next track.</summary>
    Task NextAsync(MediaSource s);
    /// <summary>Go back to the previous track.</summary>
    Task PrevAsync(MediaSource s);
    /// <summary>Jump to a position, in seconds.</summary>
    Task SeekAsync(MediaSource s, double seconds);
    /// <summary>Set the volume (0 to <see cref="MediaSource.MaxVolume"/>).</summary>
    Task SetVolumeAsync(MediaSource s, double volume);
    /// <summary>Change the repeat mode.</summary>
    Task SetLoopAsync(MediaSource s, LoopMode mode);
    /// <summary>Play the playlist item at this zero-based index.</summary>
    Task JumpAsync(MediaSource s, int index);
}

/// <summary>
/// Sends each command to the browser extension for tab sources,
/// and to Windows SMTC for everything else.
/// </summary>
public sealed class CommandRouter(ISourceCommands extension, ISourceCommands smtc) : ISourceCommands
{
    private ISourceCommands Pick(MediaSource s) => s.Kind == SourceKind.ExtensionTab ? extension : smtc;

    /// <inheritdoc />
    public Task TogglePlayAsync(MediaSource s) => Pick(s).TogglePlayAsync(s);
    /// <inheritdoc />
    public Task NextAsync(MediaSource s) => Pick(s).NextAsync(s);
    /// <inheritdoc />
    public Task PrevAsync(MediaSource s) => Pick(s).PrevAsync(s);
    /// <inheritdoc />
    public Task SeekAsync(MediaSource s, double seconds) => Pick(s).SeekAsync(s, seconds);
    /// <inheritdoc />
    public Task SetVolumeAsync(MediaSource s, double volume) => Pick(s).SetVolumeAsync(s, volume);
    /// <inheritdoc />
    public Task SetLoopAsync(MediaSource s, LoopMode mode) => Pick(s).SetLoopAsync(s, mode);
    /// <inheritdoc />
    public Task JumpAsync(MediaSource s, int index) => Pick(s).JumpAsync(s, index);
}
