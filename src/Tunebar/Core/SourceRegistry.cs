namespace Tunebar.Core;

/// <summary>
/// Merges extension (browser tab) and SMTC sources, hides the duplicate Opera entry,
/// and decides which source the popup shows (<see cref="Current"/>) and which are the rest
/// (<see cref="Others"/>). A source can be locked so it stays Current.
/// Not thread-safe by design: the UI layer must call every member from one thread.
/// </summary>
public sealed class SourceRegistry
{
    // How long a locked source may be missing (extension reconnecting) before the lock is dropped.
    private static readonly TimeSpan LockGracePeriod = TimeSpan.FromSeconds(10);
    private static readonly string[] TitleSuffixes = [" - YouTube Music", " - YouTube"];

    private readonly Func<DateTimeOffset> _now;

    private IReadOnlyList<MediaSource> _extensionSources = [];
    private IReadOnlyList<MediaSource> _smtcSources = [];
    private readonly SourceGrace _tabGrace = new();
    private IReadOnlyList<MediaSource> _extensionInput = []; // the latest list exactly as the extension sent it

    // Bookkeeping per source id.
    private readonly Dictionary<string, DateTimeOffset> _startedAt = new();
    private readonly Dictionary<string, DateTimeOffset> _pausedAt = new();
    private readonly Dictionary<string, bool> _wasPlaying = new();

    private string? _selectedId;
    private DateTimeOffset? _lockMissingSince; // set while the locked source is not visible

    // What we last told listeners, so Changed fires only on a real change.
    private IReadOnlyList<MediaSource> _lastSnapshot = [];
    private string? _lastLockedId;

    /// <param name="now">Clock, injected so tests can control time.</param>
    public SourceRegistry(Func<DateTimeOffset> now) => _now = now;

    /// <summary>Raised when Current, Others or LockedId changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Id of the locked source, or null when nothing is locked.</summary>
    public string? LockedId { get; private set; }

    /// <summary>The source the popup shows, or null when there are no sources.</summary>
    public MediaSource? Current => Ordered().FirstOrDefault();

    /// <summary>Visible sources other than Current: playing ones first, then the most recent.</summary>
    public IReadOnlyList<MediaSource> Others => Ordered().Skip(1).ToList();

    /// <summary>
    /// Replaces all browser-tab sources. A tab that is missing from the new list stays for a few
    /// seconds (see <see cref="SourceGrace"/>), so a brief gap during a track change or a restart of
    /// the extension's worker does not make the popup and widget flash "nothing playing".
    /// </summary>
    public void SetExtensionSources(IReadOnlyList<MediaSource> sources)
    {
        _extensionInput = sources;
        ApplyTabGrace();
        Refresh();
    }

    /// <summary>Replaces all SMTC sources.</summary>
    public void SetSmtcSources(IReadOnlyList<MediaSource> sources)
    {
        _smtcSources = sources;
        ApplyTabGrace(); // also expires kept tabs when the extension went quiet (disconnected)
        Refresh();
    }

    private void ApplyTabGrace()
    {
        var built = new Dictionary<string, MediaSource?>();
        foreach (var source in _extensionInput) built[source.Id] = source;
        _extensionSources = _tabGrace.Update(built, _now());
    }

    /// <summary>Removes all browser-tab sources (for example when the extension disconnects).</summary>
    public void ClearExtensionSources() => SetExtensionSources([]);

    /// <summary>Keeps this source as Current, even when others start playing. No-op if it is not visible.</summary>
    public void Lock(string id)
    {
        if (!Visible().Any(s => s.Id == id)) return;
        LockedId = id;
        _lockMissingSince = null;
        Notify();
    }

    /// <summary>Releases the lock.</summary>
    public void Unlock()
    {
        LockedId = null;
        _lockMissingSince = null;
        Notify();
    }

    /// <summary>Shows this source until another source starts playing. No-op if it is not visible.</summary>
    public void Select(string id)
    {
        if (!Visible().Any(s => s.Id == id)) return;
        _startedAt[id] = _now();
        _selectedId = id;
        Notify();
    }

    /// <summary>
    /// Extension sources plus SMTC sources, minus the Opera SMTC duplicate of a tab.
    /// The duplicate's cover bytes (Opera already downloaded the picture for Windows) are given to
    /// the matching tab, so its cover shows at once instead of waiting for a download of the URL.
    /// </summary>
    private List<MediaSource> Visible()
    {
        var operaByTitle = new Dictionary<string, MediaSource>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in _smtcSources.Where(s => s.AppName == "Opera"))
            operaByTitle.TryAdd(NormalizeTitle(s.Title), s);

        var tabs = _extensionSources.Select(tab =>
            operaByTitle.TryGetValue(NormalizeTitle(tab.Title), out var twin) && twin.CoverBytes is { Length: > 0 }
                ? tab with { CoverBytes = twin.CoverBytes }
                : tab).ToList();

        var tabTitles = _extensionSources
            .Select(s => NormalizeTitle(s.Title))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var smtc = _smtcSources.Where(s =>
            !(s.AppName == "Opera" && tabTitles.Contains(NormalizeTitle(s.Title))));

        return tabs.Concat(smtc).ToList();
    }

    /// <summary>Trims the title and removes a trailing " - YouTube" / " - YouTube Music" (Opera adds it).</summary>
    private static string NormalizeTitle(string title)
    {
        var trimmed = title.Trim();
        foreach (var suffix in TitleSuffixes)
        {
            if (trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return trimmed[..^suffix.Length].Trim();
        }
        return trimmed;
    }

    /// <summary>
    /// Visible sources in display order; the first one is Current.
    /// Rule without lock: playing sources first (latest StartedAt first); then paused
    /// sources (latest PausedAt first). A locked source goes to the front; a source
    /// chosen with Select goes to the front until another source starts after it.
    /// </summary>
    private List<MediaSource> Ordered()
    {
        var visible = Visible();
        var selectedIsActive = IsSelectionActive(visible);

        var ordered = visible
            .OrderByDescending(s => s.Id == LockedId)
            .ThenByDescending(s => selectedIsActive && s.Id == _selectedId)
            .ThenByDescending(s => s.IsPlaying)
            .ThenByDescending(s => s.IsPlaying ? StartedOf(s.Id) : PausedOf(s.Id))
            .ToList();
        return ordered;
    }

    /// <summary>A Select stays in effect while no other visible source started later.</summary>
    private bool IsSelectionActive(List<MediaSource> visible)
    {
        if (_selectedId is null || visible.All(s => s.Id != _selectedId)) return false;
        var selectedStart = StartedOf(_selectedId);
        return visible.Where(s => s.Id != _selectedId).All(s => StartedOf(s.Id) < selectedStart);
    }

    private DateTimeOffset StartedOf(string id) =>
        _startedAt.TryGetValue(id, out var t) ? t : DateTimeOffset.MinValue;

    // A source that was never paused (e.g. first seen paused) falls back to its start time.
    private DateTimeOffset PausedOf(string id) =>
        _pausedAt.TryGetValue(id, out var t) ? t : StartedOf(id);

    /// <summary>Updates StartedAt/PausedAt from the new data, drops a vanished lock, then notifies.</summary>
    private void Refresh()
    {
        var now = _now();
        var all = _extensionSources.Concat(_smtcSources).ToList();

        foreach (var source in all)
        {
            var known = _wasPlaying.TryGetValue(source.Id, out var before);
            if (source.IsPlaying && (!known || !before)) _startedAt[source.Id] = now;
            else if (!source.IsPlaying && known && before) _pausedAt[source.Id] = now;
            _wasPlaying[source.Id] = source.IsPlaying;
        }

        // Forget ids that are gone.
        var present = all.Select(s => s.Id).ToHashSet();
        foreach (var id in _wasPlaying.Keys.Where(id => !present.Contains(id)).ToList())
        {
            _wasPlaying.Remove(id);
            _startedAt.Remove(id);
            _pausedAt.Remove(id);
        }

        MoveLockToTab();
        UpdateLock(now);
        if (_selectedId is not null && !present.Contains(_selectedId)) _selectedId = null;

        Notify();
    }

    /// <summary>
    /// A lock on the Opera SMTC entry would point at an invisible source once that entry becomes the
    /// hidden twin of a tab (same title), so the lock moves to that tab.
    /// </summary>
    private void MoveLockToTab()
    {
        var locked = _smtcSources.FirstOrDefault(s => s.Id == LockedId && s.AppName == "Opera");
        if (locked is null) return;

        var title = NormalizeTitle(locked.Title);
        var twinTab = _extensionSources.FirstOrDefault(
            tab => string.Equals(NormalizeTitle(tab.Title), title, StringComparison.OrdinalIgnoreCase));
        if (twinTab is null) return;

        LockedId = twinTab.Id;
        _lockMissingSince = null;
    }

    /// <summary>
    /// A locked source that went missing keeps its lock for <see cref="LockGracePeriod"/> (the
    /// extension may just be reconnecting). If it is still not back after that, the lock is released.
    /// </summary>
    private void UpdateLock(DateTimeOffset now)
    {
        if (LockedId is null)
        {
            _lockMissingSince = null;
            return;
        }

        // Check expiry first: a source that comes back only after the grace period is too late.
        if (_lockMissingSince is { } since && now - since > LockGracePeriod)
        {
            LockedId = null;
            _lockMissingSince = null;
            return;
        }

        // Measured on what the extension/Windows actually sent, not on the short tab grace,
        // so the lock's own 10 s grace starts when the source really went missing.
        var present = _extensionInput.Any(s => s.Id == LockedId) || _smtcSources.Any(s => s.Id == LockedId);
        if (present) _lockMissingSince = null;
        else _lockMissingSince ??= now;
    }

    /// <summary>Raises Changed only if the ordered list (same instances, same order) or the lock changed.</summary>
    private void Notify()
    {
        var snapshot = Ordered();
        var same = snapshot.Count == _lastSnapshot.Count
            && snapshot.Zip(_lastSnapshot).All(pair => ReferenceEquals(pair.First, pair.Second))
            && LockedId == _lastLockedId;
        if (same) return;

        _lastSnapshot = snapshot;
        _lastLockedId = LockedId;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
