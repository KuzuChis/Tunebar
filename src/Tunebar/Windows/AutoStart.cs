using System.IO;
using System.Text.Json;

namespace Tunebar.Windows;

/// <summary>
/// "Run when Windows starts" toggle. The switch itself is a startup entry (a Task Scheduler task);
/// a small settings.json remembers the choice so the first-run default is applied only once.
/// </summary>
public sealed class AutoStart
{
    private readonly IStartupEntry _entry;
    private readonly string _settingsPath;
    private readonly string _exePath;

    public AutoStart(IStartupEntry entry, string settingsPath, string exePath)
    {
        _entry = entry;
        _settingsPath = settingsPath;
        _exePath = exePath;
    }

    /// <summary>True when the startup entry exists and points at this exe.</summary>
    public bool IsEnabled => _entry.IsRegisteredFor(_exePath);

    /// <summary>Creates or removes the startup entry and saves the choice to settings.json.</summary>
    public void SetEnabled(bool on)
    {
        if (on)
            _entry.Register(_exePath);
        else
            _entry.Unregister();

        SaveSettings(on);
    }

    /// <summary>On the very first run (no readable settings.json) auto-start is switched on.</summary>
    public void ApplyFirstRunDefault()
    {
        if (!SettingsExist())
            SetEnabled(true);
    }

    /// <summary>
    /// When settings.json says auto-start is on but the startup entry is missing or points to another
    /// exe (for example the app was moved), rewrites it to this exe. Does nothing otherwise.
    /// </summary>
    public void Reconcile()
    {
        if (ReadSettings() == true && !IsEnabled)
            SetEnabled(true);
    }

    /// <summary>A missing, unreadable or corrupt settings file all count as "no settings yet".</summary>
    private bool SettingsExist() => ReadSettingsObject(out _);

    /// <summary>The saved autoStart choice; null when there are no usable settings or no such field.</summary>
    private bool? ReadSettings()
    {
        if (!ReadSettingsObject(out var autoStart))
            return null;
        return autoStart;
    }

    /// <summary>Reads settings.json. Returns false for a missing, unreadable or non-object file.</summary>
    private bool ReadSettingsObject(out bool? autoStart)
    {
        autoStart = null;
        try
        {
            if (!File.Exists(_settingsPath))
                return false;
            using var doc = JsonDocument.Parse(File.ReadAllText(_settingsPath));
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;
            if (doc.RootElement.TryGetProperty("autoStart", out var value)
                && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                autoStart = value.GetBoolean();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void SaveSettings(bool on)
    {
        var folder = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(new { autoStart = on }));
    }
}
