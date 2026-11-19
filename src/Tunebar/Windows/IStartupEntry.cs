namespace Tunebar.Windows;

/// <summary>Where "run when Windows starts" is stored. Lets tests swap the real system for a fake.</summary>
public interface IStartupEntry
{
    /// <summary>True when an entry exists and launches the given exe.</summary>
    bool IsRegisteredFor(string exePath);

    /// <summary>Creates or replaces the entry so it launches the given exe at logon.</summary>
    void Register(string exePath);

    /// <summary>Removes the entry. A missing entry is not an error.</summary>
    void Unregister();
}
