using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace Tunebar.Windows;

/// <summary>
/// One-time move from the old "MediaTray" install: copies its settings into the Tunebar folder,
/// removes the old folder and the old Run values, and re-registers auto-start as a scheduled task.
/// Never throws; safe to run on every start (it does nothing once the old traces are gone).
/// </summary>
public sealed class LegacyMigration
{
    private const string DefaultRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static readonly string[] OldRunValueNames = { "MediaTray", "Tunebar" };
    private static readonly string[] SettingsFiles = { "settings.json", "taskbar-widget.json" };

    private readonly string _oldFolder;
    private readonly string _newFolder;
    private readonly string _runKeyPath;
    private readonly AutoStart _autoStart;

    public LegacyMigration(string oldFolder, string newFolder, string runKeyPath, AutoStart autoStart)
    {
        _oldFolder = oldFolder;
        _newFolder = newFolder;
        _runKeyPath = runKeyPath;
        _autoStart = autoStart;
    }

    /// <summary>The production migration: %LOCALAPPDATA%\MediaTray into %LOCALAPPDATA%\Tunebar, HKCU Run key for cleanup.</summary>
    public static LegacyMigration CreateDefault(AutoStart autoStart)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new LegacyMigration(
            Path.Combine(local, "MediaTray"), Path.Combine(local, "Tunebar"),
            DefaultRunKeyPath, autoStart);
    }

    public void Run()
    {
        var wantAutoStart = false;

        try
        {
            wantAutoStart |= MigrateFolder();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Legacy settings migration failed: {ex.Message}");
        }

        try
        {
            wantAutoStart |= RemoveOldRunValue();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Legacy Run value cleanup failed: {ex.Message}");
        }

        if (!wantAutoStart)
            return;
        try
        {
            _autoStart.SetEnabled(true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Legacy auto-start migration failed: {ex.Message}");
        }
    }

    /// <summary>Copies the old settings when the new folder has none; returns true if they said autoStart on.</summary>
    private bool MigrateFolder()
    {
        if (!Directory.Exists(_oldFolder))
            return false;

        var autoStartWasOn = false;
        if (!File.Exists(Path.Combine(_newFolder, "settings.json")))
        {
            Directory.CreateDirectory(_newFolder);
            foreach (var name in SettingsFiles)
            {
                var source = Path.Combine(_oldFolder, name);
                if (File.Exists(source))
                    File.Copy(source, Path.Combine(_newFolder, name), overwrite: true);
            }
            autoStartWasOn = ReadAutoStart(Path.Combine(_newFolder, "settings.json"));
        }

        try
        {
            Directory.Delete(_oldFolder, recursive: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not delete old settings folder: {ex.Message}");
        }
        return autoStartWasOn;
    }

    /// <summary>
    /// Deletes the old "MediaTray" Run value and the former "Tunebar" Run value (Explorer skips it;
    /// auto-start is now a scheduled task). Returns true if either existed.
    /// </summary>
    private bool RemoveOldRunValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_runKeyPath, writable: true);
        if (key is null)
            return false;
        var existed = false;
        foreach (var name in OldRunValueNames)
        {
            if (key.GetValue(name) is null)
                continue;
            key.DeleteValue(name, throwOnMissingValue: false);
            existed = true;
        }
        return existed;
    }

    private static bool ReadAutoStart(string path)
    {
        try
        {
            if (!File.Exists(path))
                return false;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("autoStart", out var value)
                && value.ValueKind == JsonValueKind.True;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
