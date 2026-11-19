using Tunebar.Core;

namespace Tunebar.Core;

/// <summary>
/// Keeps the last good copy of each source for a short time, so a track change
/// (when an app briefly drops its session or sends an empty title) does not make the source vanish.
/// </summary>
internal sealed class SourceGrace
{
    public static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(3);

    private readonly Dictionary<string, (MediaSource Source, DateTimeOffset At)> _lastGood = new();

    /// <summary>
    /// Takes what was built this round (null = missing, failed or empty title) and returns the sources to show.
    /// A null entry reuses the last good copy for up to <see cref="GracePeriod"/>; with no copy it is skipped.
    /// </summary>
    public IReadOnlyList<MediaSource> Update(IReadOnlyDictionary<string, MediaSource?> built, DateTimeOffset now)
    {
        var result = new List<MediaSource>();
        foreach (var (key, source) in built)
        {
            if (source is not null && !string.IsNullOrWhiteSpace(source.Title))
            {
                _lastGood[key] = (source, now);
                result.Add(source);
            }
            else if (_lastGood.TryGetValue(key, out var last) && now - last.At < GracePeriod)
            {
                result.Add(last.Source);
            }
        }

        // Keys that were not reported at all count as missing too.
        foreach (var (key, last) in _lastGood.ToList())
        {
            if (built.ContainsKey(key)) continue;
            if (now - last.At < GracePeriod) result.Add(last.Source);
        }

        foreach (var key in _lastGood.Where(p => now - p.Value.At >= GracePeriod).Select(p => p.Key).ToList())
            _lastGood.Remove(key);
        return result;
    }
}
