using Tunebar.Windows;

namespace Tunebar.Tests;

/// <summary>In-memory startup entry for tests; remembers the registered exe path.</summary>
internal sealed class FakeStartupEntry : IStartupEntry
{
    public string? Registered { get; set; }

    public bool IsRegisteredFor(string exePath) =>
        string.Equals(Registered, exePath, StringComparison.OrdinalIgnoreCase);

    public void Register(string exePath) => Registered = exePath;

    public void Unregister() => Registered = null;
}
