using Tunebar.Windows;

namespace Tunebar.Tests;

public sealed class AutoStartTests : IDisposable
{
    private const string ExePath = @"C:\Apps\Tunebar\Tunebar.exe";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "TunebarTests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeStartupEntry _entry = new();
    private string SettingsPath => Path.Combine(_dir, "settings.json");

    private AutoStart Create() => new(_entry, SettingsPath, ExePath);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void First_run_enables()
    {
        var autoStart = Create();

        autoStart.ApplyFirstRunDefault();

        Assert.Equal(ExePath, _entry.Registered);
        Assert.True(autoStart.IsEnabled);
        Assert.Contains("\"autoStart\":true", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void First_run_respects_existing_settings()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, "{\"autoStart\":false}");
        var autoStart = Create();

        autoStart.ApplyFirstRunDefault();

        Assert.Null(_entry.Registered);
        Assert.False(autoStart.IsEnabled);
    }

    [Fact]
    public void SetEnabled_false_removes_entry()
    {
        var autoStart = Create();
        autoStart.SetEnabled(true);

        autoStart.SetEnabled(false);

        Assert.Null(_entry.Registered);
        Assert.False(autoStart.IsEnabled);
        Assert.Contains("\"autoStart\":false", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void Entry_pointing_elsewhere_is_not_enabled_and_is_overwritten()
    {
        _entry.Registered = @"C:\Old\Tunebar.exe";
        var autoStart = Create();

        Assert.False(autoStart.IsEnabled);
        autoStart.SetEnabled(true);

        Assert.True(autoStart.IsEnabled);
    }

    [Fact]
    public void Reconcile_rewrites_stale_path_when_enabled_in_settings()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, "{\"autoStart\":true}");
        _entry.Registered = @"C:\Old\Tunebar.exe";
        var autoStart = Create();

        autoStart.Reconcile();

        Assert.Equal(ExePath, _entry.Registered);
        Assert.True(autoStart.IsEnabled);
    }

    [Fact]
    public void Reconcile_registers_when_entry_missing()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, "{\"autoStart\":true}");
        var autoStart = Create();

        autoStart.Reconcile();

        Assert.Equal(ExePath, _entry.Registered);
    }

    [Fact]
    public void Reconcile_leaves_disabled_alone()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, "{\"autoStart\":false}");
        var autoStart = Create();

        autoStart.Reconcile();

        Assert.Null(_entry.Registered);
        Assert.False(autoStart.IsEnabled);
    }

    [Fact]
    public void Corrupt_settings_count_as_missing()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, "not json {{");
        var autoStart = Create();

        autoStart.ApplyFirstRunDefault();

        Assert.True(autoStart.IsEnabled);
        Assert.Contains("\"autoStart\":true", File.ReadAllText(SettingsPath));
    }
}
