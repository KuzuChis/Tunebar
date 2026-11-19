using System.ComponentModel;
using Tunebar.Core;
using Tunebar.Ui;

namespace Tunebar.Tests;

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
        public Task SetVolumeAsync(MediaSource s, double volume) => Record(FormattableString.Invariant($"volume {s.Id} {volume}"));
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
        double maxVolume = 1,
        IReadOnlyList<LoopMode>? loopModes = null,
        LoopMode loop = LoopMode.Off,
        Playlist? playlist = null) =>
        new($"tab:{n}", SourceKind.ExtensionTab, "Opera", "Opera · tab YouTube", $"Title {n}", null, null, null,
            playing, null, null, false, false, volume, maxVolume, loop, loopModes ?? [], playlist);

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
        registry.SetExtensionSources([Tab(1), Tab(2), Tab(3, playing: false)]);

        Assert.Equal("2 sumber lain · 1 memutar", vm.OthersText);
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

    [Fact]
    public async Task StepVolume_up_and_down_by_ten_points()
    {
        var (vm, registry, commands) = Create();
        registry.SetExtensionSources([Tab(1, volume: 0.5)]);

        await vm.StepVolumeAsync(0.1);
        await vm.StepVolumeAsync(-0.1);

        // The second step continues from the 0.6 we sent (the tab has not reported it yet).
        Assert.Equal(["volume tab:1 0.6", "volume tab:1 0.5"], commands.Calls);
    }

    [Fact]
    public async Task StepVolume_held_key_keeps_climbing_before_the_tab_reports()
    {
        var (vm, registry, commands) = Create();
        registry.SetExtensionSources([Tab(1, volume: 0.5)]);

        await vm.StepVolumeAsync(0.1);
        await vm.StepVolumeAsync(0.1);
        await vm.StepVolumeAsync(0.1);

        Assert.Equal(["volume tab:1 0.6", "volume tab:1 0.7", "volume tab:1 0.8"], commands.Calls);
    }

    [Fact]
    public async Task StepVolume_clamps_to_zero_and_max()
    {
        var (vm, registry, commands) = Create();
        registry.SetExtensionSources([Tab(1, volume: 0.05, maxVolume: 1.5)]);
        await vm.StepVolumeAsync(-0.1);

        registry.SetExtensionSources([Tab(1, volume: 1.45, maxVolume: 1.5)]);
        await vm.StepVolumeAsync(0.1);

        Assert.Equal(["volume tab:1 0", "volume tab:1 1.5"], commands.Calls);
    }

    [Fact]
    public async Task StepVolume_rounds_to_hundredths()
    {
        var (vm, registry, commands) = Create();
        registry.SetExtensionSources([Tab(1, volume: 0.3333)]);

        await vm.StepVolumeAsync(0.1);

        Assert.Equal(["volume tab:1 0.43"], commands.Calls);
    }

    [Fact]
    public async Task StepVolume_ignored_without_volume_or_source()
    {
        var (vm, registry, commands) = Create();
        await vm.StepVolumeAsync(0.1);

        registry.SetExtensionSources([Tab(1, volume: null)]);
        await vm.StepVolumeAsync(0.1);

        Assert.Empty(commands.Calls);
        Assert.Null(vm.FlashText);
    }

    [Fact]
    public async Task StepVolume_sets_FlashText()
    {
        var (vm, registry, _) = Create();
        registry.SetExtensionSources([Tab(1, volume: 0.6)]);
        var changed = new List<string?>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await vm.StepVolumeAsync(0.1);

        Assert.Equal("Volume 70%", vm.FlashText);
        Assert.Contains(nameof(PopupViewModel.FlashText), changed);
    }
}