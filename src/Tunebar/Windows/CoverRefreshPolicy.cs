using Tunebar.Core;

namespace Tunebar.Windows;

/// <summary>What we last learned about one app's cover art. Mutable; owned by the refresh loop.</summary>
internal sealed class CoverState
{
    public string Title { get; set; } = "";
    public byte[]? Bytes { get; set; }
    public DateTimeOffset ReadAt { get; set; }
    /// <summary>When this title first became current.</summary>
    public DateTimeOffset TitleSince { get; set; }
    /// <summary>Windows said the media properties changed, so the thumbnail may be new.</summary>
    public bool Dirty { get; set; }
}

/// <summary>Decides when the thumbnail must be read again (pure, so it can be unit tested).</summary>
internal static class CoverRefreshPolicy
{
    public static readonly TimeSpan RetryEvery = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan RetryWindow = TimeSpan.FromSeconds(30);

    /// <summary>True when the thumbnail should be read now: new title, marked dirty, or a missing cover still worth retrying.</summary>
    public static bool ShouldRead(CoverState? state, string title, DateTimeOffset now)
    {
        if (state is null) return true;
        if (state.Title != title) return true;
        if (state.Dirty) return true;
        return state.Bytes is null
            && now - state.ReadAt >= RetryEvery
            && now - state.TitleSince < RetryWindow;
    }
}
