using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace Tunebar.Windows;

/// <summary>
/// One-time move from the old "MediaTray" install: copies its settings into the Tunebar folder,
/// removes the old folder and the old Run value, and re-registers auto-start under the new name.
/// Never throws; safe to run on every start (it does nothing once the old traces are gone).
/// </summary>
public sealed class LegacyMigration
{
    private const string OldRunValueName = "MediaTray";
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

    /// <summary>The production migration: %LOCALAPPDATA%\MediaTray into %LOCALAPPDATA%\Tunebar, HKCU Run key.</summary>
    public static LegacyMigration CreateDefault(AutoStart autoStart)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new LegacyMigration(
            Path.Combine(local, "MediaTray"), Path.Combine(local, "Tunebar"),
            AutoStart.DefaultRunKeyPath, autoStart);
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

    /// <summary>Deletes the old Run value; returns true if it existed.</summary>
    private bool RemoveOldRunValue()
    {
        using var key = Registry.CurrentUser.OpenSubKey(_runKeyPath, writable: true);
        if (key?.GetValue(OldRunValueName) is null)
            return false;
        key.DeleteValue(OldRunValueName, throwOnMissingValue: false);
        return true;
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
