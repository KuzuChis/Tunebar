using System.Text.Json;
using MediaTray.Core;

namespace MediaTray.Tests;

public class ProtocolTests
{
    private const string FullJson = """
    {"type":"tabs","tabs":[{"tabId":12,"site":"youtube","title":"Song","artist":"Band","coverUrl":"https://x/c.jpg",
      "isPlaying":true,"position":84.2,"duration":221.0,"volume":0.7,
      "loop":"off","loopModes":["off","playlist","one"],"canNext":true,"canPrev":true,
      "playlist":{"index":2,"items":[{"title":"A","artist":"X"},{"title":"B","artist":null},{"title":"C"}]}}]}
    """;

    private static string OneTab(string site, string duration, string volume, string playlist) =>
        "{\"type\":\"tabs\",\"tabs\":[{\"tabId\":5,\"site\":\"" + site + "\",\"title\":\"T\"," +
        "\"isPlaying\":false,\"position\":1,\"duration\":" + duration + ",\"volume\":" + volume + "," +
        "\"loop\":\"off\",\"loopModes\":[\"off\"],\"canNext\":false,\"canPrev\":false,\"playlist\":" + playlist + "}]}";

    [Fact]
    public void ParseTabs_maps_all_fields()
    {
        var tabs = Protocol.ParseTabs(FullJson);

        var s = Assert.Single(tabs!);
        Assert.Equal("tab:12", s.Id);
        Assert.Equal(SourceKind.ExtensionTab, s.Kind);
        Assert.Equal("Opera", s.AppName);
        Assert.Equal("Opera · tab YouTube", s.OriginLabel);
        Assert.Equal("Song", s.Title);
        Assert.Equal("Band", s.Artist);
        Assert.Equal("https://x/c.jpg", s.CoverUrl);
        Assert.Null(s.CoverBytes);
        Assert.True(s.IsPlaying);
        Assert.Equal(84.2, s.PositionSec);
        Assert.Equal(221.0, s.DurationSec);
        Assert.True(s.CanNext);
        Assert.True(s.CanPrev);
        Assert.Equal(0.7, s.Volume);
        Assert.Equal(1.5, s.MaxVolume);
        Assert.Equal(LoopMode.Off, s.Loop);
        Assert.Equal(3, s.LoopModes.Count);
        Assert.Equal(2, s.Playlist!.Index);
        Assert.Equal(3, s.Playlist.Items.Count);
        Assert.Equal("A", s.Playlist.Items[0].Title);
    }

    [Fact]
    public void ParseTabs_ytmusic_label()
    {
        var s = Assert.Single(Protocol.ParseTabs(OneTab("ytmusic", "10", "0.5", "null"))!);
        Assert.Equal("Opera · YouTube Music", s.OriginLabel);
    }

    [Fact]
    public void ParseTabs_null_duration_disables_seek()
    {
        var s = Assert.Single(Protocol.ParseTabs(OneTab("youtube", "null", "0.5", "null"))!);
        Assert.Null(s.DurationSec);
        Assert.False(s.CanSeek);
    }

    [Fact]
    public void ParseTabs_null_volume_and_playlist()
    {
        var s = Assert.Single(Protocol.ParseTabs(OneTab("youtube", "10", "null", "null"))!);
        Assert.Null(s.Volume);
        Assert.Null(s.Playlist);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"type":"hello"}""")]
    public void ParseTabs_returns_null_for_garbage(string json) =>
        Assert.Null(Protocol.ParseTabs(json));

    [Fact]
    public void BuildCommand_seek()
    {
        using var doc = JsonDocument.Parse(Protocol.BuildCommand(12, "seek", 84.5));
        var root = doc.RootElement;
        Assert.Equal("command", root.GetProperty("type").GetString());
        Assert.Equal(12, root.GetProperty("tabId").GetInt32());
        Assert.Equal("seek", root.GetProperty("action").GetString());
        Assert.Equal(84.5, root.GetProperty("value").GetDouble());
    }

    [Fact]
    public void BuildCommand_loop_uses_lowercase_string()
    {
        using var doc = JsonDocument.Parse(Protocol.BuildCommand(12, "loop", LoopMode.Playlist));
        Assert.Equal("playlist", doc.RootElement.GetProperty("value").GetString());
    }

    [Fact]
    public void TabIdOf_parses_and_rejects()
    {
        Assert.Equal(12, Protocol.TabIdOf(Make("tab:12", SourceKind.ExtensionTab)));
        Assert.Throws<ArgumentException>(() => Protocol.TabIdOf(Make("smtc:abc", SourceKind.Smtc)));
    }

    [Fact]
    public async Task Router_dispatches_by_kind()
    {
        var ext = new FakeCommands();
        var smtc = new FakeCommands();
        var router = new CommandRouter(ext, smtc);

        await router.TogglePlayAsync(Make("tab:1", SourceKind.ExtensionTab));
        await router.NextAsync(Make("smtc:x", SourceKind.Smtc));

        Assert.Equal(["toggle"], ext.Calls);
        Assert.Equal(["next"], smtc.Calls);
    }

    private static MediaSource Make(string id, SourceKind kind) =>
        new(id, kind, "App", "App", "T", null, null, null, false, null, null,
            false, false, null, 1.0, LoopMode.Off, [LoopMode.Off], null);

    private sealed class FakeCommands : ISourceCommands
    {
        public List<string> Calls { get; } = [];
        public Task TogglePlayAsync(MediaSource s) => Record("toggle");
        public Task NextAsync(MediaSource s) => Record("next");
        public Task PrevAsync(MediaSource s) => Record("prev");
        public Task SeekAsync(MediaSource s, double seconds) => Record("seek");
        public Task SetVolumeAsync(MediaSource s, double volume) => Record("volume");
        public Task SetLoopAsync(MediaSource s, LoopMode mode) => Record("loop");
        public Task JumpAsync(MediaSource s, int index) => Record("jump");
        private Task Record(string name) { Calls.Add(name); return Task.CompletedTask; }
    }
}
