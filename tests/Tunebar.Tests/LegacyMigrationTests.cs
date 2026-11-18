using Microsoft.Win32;
using Tunebar.Windows;

namespace Tunebar.Tests;

public sealed class LegacyMigrationTests : IDisposable
{
    private readonly string _root = @"Software\TunebarTests\" + Guid.NewGuid().ToString("N");
    private string RunKeyPath => _root + @"\Run";
    private const string ExePath = @"C:\Apps\Tunebar\Tunebar.exe";

    private readonly string _base = Path.Combine(Path.GetTempPath(), "TunebarTests-" + Guid.NewGuid().ToString("N"));
    private string OldDir => Path.Combine(_base, "MediaTray");
    private string NewDir => Path.Combine(_base, "Tunebar");

    private LegacyMigration Create() =>
        new(OldDir, NewDir, RunKeyPath, new AutoStart(RunKeyPath, Path.Combine(NewDir, "settings.json"), ExePath));

    private string? RunValue(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(name) as string;
    }

    private void SetOldRun()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key.SetValue("MediaTray", "\"C:\\Old\\MediaTray.exe\"");
    }

    private void WriteOld(string name, string content)
    {
        Directory.CreateDirectory(OldDir);
        File.WriteAllText(Path.Combine(OldDir, name), content);
    }

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_root, throwOnMissingSubKey: false);
        if (Directory.Exists(_base)) Directory.Delete(_base, recursive: true);
    }

    [Fact]
    public void Copies_settings_and_removes_old_folder()
    {
        WriteOld("settings.json", "{\"autoStart\":false}");
        WriteOld("taskbar-widget.json", "{\"showInTaskbar\":false}");

        Create().Run();

        Assert.Equal("{\"autoStart\":false}", File.ReadAllText(Path.Combine(NewDir, "settings.json")));
        Assert.Equal("{\"showInTaskbar\":false}", File.ReadAllText(Path.Combine(NewDir, "taskbar-widget.json")));
        Assert.False(Directory.Exists(OldDir));
        Assert.Null(RunValue("Tunebar"));
    }

    [Fact]
    public void Keeps_existing_new_settings()
    {
        WriteOld("settings.json", "{\"autoStart\":false}");
        Directory.CreateDirectory(NewDir);
        File.WriteAllText(Path.Combine(NewDir, "settings.json"), "{\"autoStart\":true}");

        Create().Run();

        Assert.Equal("{\"autoStart\":true}", File.ReadAllText(Path.Combine(NewDir, "settings.json")));
        Assert.False(Directory.Exists(OldDir));
    }

    [Fact]
    public void Old_autostart_on_registers_new_run_value()
    {
        WriteOld("settings.json", "{\"autoStart\":true}");

        Create().Run();

        Assert.Equal($"\"{ExePath}\"", RunValue("Tunebar"));
    }

    [Fact]
    public void Old_run_value_is_replaced_by_new_one()
    {
        SetOldRun();

        Create().Run();

        Assert.Null(RunValue("MediaTray"));
        Assert.Equal($"\"{ExePath}\"", RunValue("Tunebar"));
        Assert.Contains("\"autoStart\":true", File.ReadAllText(Path.Combine(NewDir, "settings.json")));
    }

    [Fact]
    public void Nothing_to_migrate_does_nothing()
    {
        Create().Run();

        Assert.False(Directory.Exists(NewDir));
        Assert.Null(RunValue("Tunebar"));
    }

    [Fact]
    public void Corrupt_old_settings_do_not_throw()
    {
        WriteOld("settings.json", "not json");

        var ex = Record.Exception(() => Create().Run());

        Assert.Null(ex);
        Assert.Null(RunValue("Tunebar"));
    }
}
