using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tunebar.Windows;

/// <summary>
/// Remembers whether the taskbar widget is switched on, in a small json file.
/// A missing or unreadable file means "on".
/// </summary>
public sealed class WidgetSettings
{
    private readonly string _path;

    public WidgetSettings(string path) => _path = path;

    /// <summary>The production location: %LOCALAPPDATA%\Tunebar\taskbar-widget.json.</summary>
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tunebar", "taskbar-widget.json");

    public bool ShowInTaskbar
    {
        get
        {
            try
            {
                if (!File.Exists(_path)) return true;
                var node = JsonNode.Parse(File.ReadAllText(_path));
                return node?["showInTaskbar"]?.GetValue<bool>() ?? true;
            }
            catch (Exception)
            {
                return true;
            }
        }
    }

    public void Save(bool showInTaskbar)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(new { showInTaskbar }));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Saving widget settings failed: {ex.Message}");
        }
    }
}
