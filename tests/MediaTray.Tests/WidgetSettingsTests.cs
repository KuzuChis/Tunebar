using System.IO;
using MediaTray.Windows;

namespace MediaTray.Tests;

public class WidgetSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "MediaTrayTests-" + Guid.NewGuid());

    private string FilePath => Path.Combine(_dir, "taskbar-widget.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Missing_file_means_true() => Assert.True(new WidgetSettings(FilePath).ShowInTaskbar);

    [Fact]
    public void Corrupt_file_means_true()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{ not json");
        Assert.True(new WidgetSettings(FilePath).ShowInTaskbar);
    }

    [Fact]
    public void Wrong_value_type_means_true()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, "{\"showInTaskbar\": \"no\"}");
        Assert.True(new WidgetSettings(FilePath).ShowInTaskbar);
    }

    [Fact]
    public void Saved_false_is_read_back()
    {
        new WidgetSettings(FilePath).Save(false);
        Assert.False(new WidgetSettings(FilePath).ShowInTaskbar);
    }

    [Fact]
    public void Saved_true_is_read_back()
    {
        var settings = new WidgetSettings(FilePath);
        settings.Save(false);
        settings.Save(true);
        Assert.True(new WidgetSettings(FilePath).ShowInTaskbar);
    }
}
