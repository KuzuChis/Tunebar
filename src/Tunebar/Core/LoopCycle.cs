namespace Tunebar.Core;

/// <summary>Picks the next loop mode when the user clicks the loop button.</summary>
public static class LoopCycle
{
    /// <summary>
    /// Cycles Off -> Playlist -> One -> Off, skipping modes the player does not support.
    /// Returns <paramref name="current"/> when nothing else is supported.
    /// </summary>
    public static LoopMode Next(LoopMode current, IReadOnlyList<LoopMode> supported)
    {
        var modeCount = Enum.GetValues<LoopMode>().Length;

        // Try each following mode in order, wrapping around, until one is supported.
        for (var step = 1; step <= modeCount; step++)
        {
            var candidate = (LoopMode)(((int)current + step) % modeCount);
            if (supported.Contains(candidate))
                return candidate;
        }

        return current;
    }
}
