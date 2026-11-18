using System.ComponentModel;
using MediaTray.Core;
using MediaTray.Ui;

namespace MediaTray.Tests;

public class PopupViewModelTests
{
    /// <summary>Records every command so tests can check what the view model asked for.</summary>
    private sealed class FakeCommands : ISourceCommands
    {
        public List<string> Calls { get; } = [];

        public Task TogglePlayAsync(MediaSource s) => Record($"toggle {s.Id}");
        public Task NextAsync(MediaSource s) => Record($"next {s.Id}");
        public Task PrevAsync(MediaSource s) => Record($"prev {s.Id}");
        public Task SeekAsync(MediaSource s, double seconds) => Record($"seek {s.Id} {seconds}");
        public Task SetVolumeAsync(MediaSource s, double volume) => Record($"volume {s.Id} {volume}");
        public Task SetLoopAsync(MediaSource s, LoopMode mode) => Record($"loop {s.Id} {mode}");
        public Task JumpAsync(MediaSource s, int index) => Record($"jump {s.Id} {index}");

        private Task Record(string call)
        {
            Calls.Add(call);
            return Task.CompletedTask;
        }
    }

    private static MediaSource Tab(
        int n,
        bool playing = true,
        double? volume = null,
        IReadOnlyList<LoopMode>? loopModes = null,
        LoopMode loop = LoopMode.Off,
        Playlist? playlist = null) =>
        new($"tab:{n}", SourceKind.ExtensionTab, "Opera", "Opera · tab YouTube", $"Title {n}", null, null, null,
            playing, null, null, false, false, volume, 1, loop, loopModes ?? [], playlist);

    private static (PopupViewModel Vm, SourceRegistry Registry, FakeCommands Commands) Create()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var registry = new SourceRegistry(() => now = now.AddSeconds(1));
        var commands = new FakeCommands();
        var vm = new PopupViewModel(registry, commands, action => action());
        return (vm, registry, commands);
    }

    [Fact]
    public async Task CycleLoop_sends_next_mode()
    {
        var (vm, registry, commands) = Create();
        registry.SetExtensionSources([Tab(1, loopModes: [LoopMode.Off, LoopMode.Playlist, LoopMode.One])]);

        await vm.CycleLoopAsync();

        Assert.Equal(["loop tab:1 Playlist"], commands.Calls);
    }

    [Fact]
    public void ToggleLock_locks_current_then_unlocks()
    {
        var (vm, registry, _) = Create();
        registry.SetExtensionSources([Tab(1)]);

        vm.ToggleLock();
        Assert.True(vm.IsLocked);
        Assert.Equal("tab:1", registry.LockedId);

        vm.ToggleLock();
        Assert.False(vm.IsLocked);
        Assert.Null(registry.LockedId);
    }

    [Fact]
    public void ShowVolume_false_when_volume_null()
    {
        var (vm, registry, _) = Create();
        registry.SetExtensionSources([Tab(1, volume: null)]);
        Assert.False(vm.ShowVolume);

        registry.SetExtensionSources([Tab(1, volume: 0.5)]);
        Assert.True(vm.ShowVolume);
    }

    [Fact]
    public void ShowLoop_false_when_no_loop_modes()
    {
        var (vm, registry, _) = Create();
        registry.SetExtensionSources([Tab(1, loopModes: [])]);
        Assert.False(vm.ShowLoop);

        registry.SetExtensionSources([Tab(1, loopModes: [LoopMode.Off, LoopMode.One])]);
        Assert.True(vm.ShowLoop);
    }

    [Fact]
    public void PlaylistHeader_formats()
    {
        var (vm, registry, _) = Create();
        var items = Enumerable.Range(1, 12).Select(i => new PlaylistItem($"Song {i}", null)).ToList();
        registry.SetExtensionSources([Tab(1, playlist: new Playlist(2, items))]);

        Assert.True(vm.ShowPlaylist);
        Assert.Equal("Playlist · 3 dari 12", vm.PlaylistHeader);
    }

    [Fact]
    public void OthersText_counts_others()
    {
        var (vm, registry, _) = Create();
        registry.SetExtensionSources([Tab(1)]);
        registry.SetExtensionSources([Tab(1), Tab(2), Tab(3)]);

        Assert.Equal("2 sumber lain memutar", vm.OthersText);
        Assert.Equal(2, vm.Others.Count);
    }

    [Fact]
    public void Raises_PropertyChanged_when_registry_changes()
    {
        var (vm, registry, _) = Create();
        var changed = new List<string?>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        registry.SetExtensionSources([Tab(1)]);

        Assert.Contains(nameof(PopupViewModel.Current), changed);
        Assert.Contains(nameof(PopupViewModel.OthersText), changed);
    }

    [Fact]
    public void LockSource_makes_other_current_and_locked()
    {
        var (vm, registry, _) = Create();
        registry.SetExtensionSources([Tab(1)]);
        registry.SetExtensionSources([Tab(1), Tab(2)]); // tab 2 starts later, so it is shown first
        Assert.Equal("tab:2", vm.Current?.Id);

        vm.LockSource("tab:1");

        Assert.Equal("tab:1", vm.Current?.Id);
        Assert.True(vm.IsLocked);
    }

    [Fact]
    public async Task TogglePlayOther_targets_that_source()
    {
        var (vm, registry, commands) = Create();
        registry.SetExtensionSources([Tab(1)]);
        registry.SetExtensionSources([Tab(1), Tab(2)]); // tab 2 is Current, tab 1 is "other"

        await vm.TogglePlayOtherAsync("tab:1");

        Assert.Equal(["toggle tab:1"], commands.Calls);
    }
}
