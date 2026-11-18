using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MediaTray.Bridge;
using MediaTray.Core;

namespace MediaTray.Tests;

public class ExtensionServerTests : IDisposable
{
    private const string GoodOrigin = "chrome-extension://" + ExtensionIdentity.Id;
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private const string TabsJson =
        """{"type":"tabs","tabs":[{"tabId":12,"site":"youtube","title":"Song","isPlaying":true}]}""";

    private readonly int _port = FreePort();
    private readonly ExtensionServer _server;

    public ExtensionServerTests()
    {
        _server = new ExtensionServer(_port, new OriginValidator(ExtensionIdentity.Id));
        _server.Start();
    }

    public void Dispose() => _server.Dispose();

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private async Task<ClientWebSocket> ConnectAsync(string origin = GoodOrigin)
    {
        var client = new ClientWebSocket();
        client.Options.SetRequestHeader("Origin", origin);
        using var cts = new CancellationTokenSource(Wait);
        await client.ConnectAsync(new Uri($"ws://localhost:{_port}/"), cts.Token);
        return client;
    }

    private static Task SendTextAsync(ClientWebSocket client, string text) =>
        client.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, CancellationToken.None);

    private static async Task<string> ReceiveTextAsync(ClientWebSocket client)
    {
        using var cts = new CancellationTokenSource(Wait);
        var buffer = new byte[4096];
        var result = await client.ReceiveAsync(buffer, cts.Token);
        return Encoding.UTF8.GetString(buffer, 0, result.Count);
    }

    private static async Task<T> WithTimeout<T>(Task<T> task)
    {
        var finished = await Task.WhenAny(task, Task.Delay(Wait));
        Assert.Same(task, finished);
        return await task;
    }

    private static MediaSource Tab12() => Protocol.ParseTabs(TabsJson)![0];

    [Fact]
    public async Task Server_rejects_wrong_origin()
    {
        await Assert.ThrowsAsync<WebSocketException>(() => ConnectAsync("https://evil.example"));
        Assert.False(_server.IsConnected);
    }

    [Fact]
    public async Task Server_raises_TabsReceived()
    {
        var received = new TaskCompletionSource<IReadOnlyList<MediaSource>>();
        _server.TabsReceived += tabs => received.TrySetResult(tabs);

        using var client = await ConnectAsync();
        await SendTextAsync(client, TabsJson);

        var tabs = await WithTimeout(received.Task);
        Assert.Single(tabs);
        Assert.Equal("tab:12", tabs[0].Id);
        Assert.True(_server.IsConnected);
    }

    [Fact]
    public async Task Server_ignores_garbage_messages()
    {
        var received = new TaskCompletionSource<IReadOnlyList<MediaSource>>();
        _server.TabsReceived += tabs => received.TrySetResult(tabs);

        using var client = await ConnectAsync();
        await SendTextAsync(client, "not json at all");
        await SendTextAsync(client, TabsJson);

        var tabs = await WithTimeout(received.Task);
        Assert.Single(tabs);
    }

    [Fact]
    public async Task Server_sends_command()
    {
        using var client = await ConnectAsync();
        await WaitUntilConnectedAsync();

        await _server.TogglePlayAsync(Tab12());

        using var doc = JsonDocument.Parse(await ReceiveTextAsync(client));
        Assert.Equal("toggle", doc.RootElement.GetProperty("action").GetString());
        Assert.Equal(12, doc.RootElement.GetProperty("tabId").GetInt32());
    }

    [Fact]
    public async Task Command_without_client_is_ignored()
    {
        await _server.NextAsync(Tab12());
    }

    [Fact]
    public async Task Server_raises_Disconnected_on_close()
    {
        var disconnected = new TaskCompletionSource();
        _server.Disconnected += () => disconnected.TrySetResult();

        using var client = await ConnectAsync();
        await WaitUntilConnectedAsync();
        await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);

        await WithTimeout(disconnected.Task.ContinueWith(_ => true));
        Assert.False(_server.IsConnected);
    }

    [Fact]
    public async Task New_connection_replaces_old_without_Disconnected()
    {
        var disconnectedCount = 0;
        _server.Disconnected += () => Interlocked.Increment(ref disconnectedCount);

        using var first = await ConnectAsync();
        await WaitUntilConnectedAsync();
        using var second = await ConnectAsync();

        // The old client is closed by the server.
        using var cts = new CancellationTokenSource(Wait);
        var buffer = new byte[256];
        var result = await first.ReceiveAsync(buffer, cts.Token);
        Assert.Equal(WebSocketMessageType.Close, result.MessageType);

        // The new client works.
        var received = new TaskCompletionSource<IReadOnlyList<MediaSource>>();
        _server.TabsReceived += tabs => received.TrySetResult(tabs);
        await SendTextAsync(second, TabsJson);
        await WithTimeout(received.Task);

        Assert.Equal(0, Volatile.Read(ref disconnectedCount));
        Assert.True(_server.IsConnected);
    }

    private async Task WaitUntilConnectedAsync()
    {
        var deadline = DateTime.UtcNow + Wait;
        while (!_server.IsConnected && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        Assert.True(_server.IsConnected);
    }
}
