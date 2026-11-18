using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace Tunebar.Windows;

/// <summary>
/// "Run when Windows starts" toggle. The switch itself is a value in the current user's Run key;
/// a small settings.json remembers the choice so the first-run default is applied only once.
/// </summary>
public sealed class AutoStart
{
    /// <summary>The production Run key, under HKEY_CURRENT_USER.</summary>
    public const string DefaultRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private const string ValueName = "Tunebar";

    private readonly string _runKeyPath;
    private readonly string _settingsPath;
    private readonly string _exePath;

    public AutoStart(string runKeyPath, string settingsPath, string exePath)
    {
        _runKeyPath = runKeyPath;
        _settingsPath = settingsPath;
        _exePath = exePath;
    }

    private string RunValue => $"\"{_exePath}\"";

    /// <summary>True when the Run value exists and points at this exe.</summary>
    public bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(_runKeyPath);
                return key?.GetValue(ValueName) is string value && value == RunValue;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>Writes or removes the Run value and saves the choice to settings.json.</summary>
    public void SetEnabled(bool on)
    {
        if (on)
        {
            using var key = Registry.CurrentUser.CreateSubKey(_runKeyPath);
            key.SetValue(ValueName, RunValue);
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(_runKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }

        SaveSettings(on);
    }

    /// <summary>On the very first run (no readable settings.json) auto-start is switched on.</summary>
    public void ApplyFirstRunDefault()
    {
        if (!SettingsExist())
            SetEnabled(true);
    }

    /// <summary>
    /// When settings.json says auto-start is on but the Run value is missing or points to another
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
