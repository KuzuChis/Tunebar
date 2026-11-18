namespace Tunebar.Bridge;

/// <summary>Accepts only the Origin header of our own browser extension.</summary>
public sealed class OriginValidator(string extensionId)
{
    private readonly string _allowed = "chrome-extension://" + extensionId;

    /// <summary>True only for exactly <c>chrome-extension://&lt;id&gt;</c>.</summary>
    public bool IsAllowed(string? origin) => string.Equals(origin, _allowed, StringComparison.Ordinal);
}
