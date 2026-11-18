using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using MediaTray.Core;

namespace MediaTray.Bridge;

/// <summary>
/// Local WebSocket server the browser extension connects to.
/// Only one client is active at a time; a new valid connection replaces the old one.
/// Events are raised from background threads.
/// </summary>
public sealed class ExtensionServer : ISourceCommands, IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly OriginValidator _validator;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly object _clientLock = new();
    private WebSocket? _client;
    private bool _disposed;

    public ExtensionServer(int port, OriginValidator validator)
    {
        _validator = validator;
        _listener.Prefixes.Add($"http://localhost:{port}/");
    }

    /// <summary>Raised with the full list of tabs each time the extension sends a snapshot.</summary>
    public event Action<IReadOnlyList<MediaSource>>? TabsReceived;

    /// <summary>Raised when the active client goes away (not when it is replaced by a new one).</summary>
    public event Action? Disconnected;

    /// <summary>True while an extension client is connected.</summary>
    public bool IsConnected
    {
        get { lock (_clientLock) return _client is { State: WebSocketState.Open }; }
    }

    /// <summary>Starts listening in the background.</summary>
    public void Start()
    {
        _listener.Start();
        _ = Task.Run(AcceptLoopAsync);
    }

    // ---- Accepting connections ----

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception) when (_stop.IsCancellationRequested || !_listener.IsListening)
            {
                return; // listener was stopped
            }
            catch (Exception)
            {
                continue; // a broken request must not stop the server
            }
            _ = Task.Run(() => HandleContextAsync(context));
        }
    }

    private async Task HandleContextAsync(HttpListenerContext context)
    {
        try
        {
            if (!context.Request.IsWebSocketRequest)
            {
                Reject(context, 400);
                return;
            }
            if (!_validator.IsAllowed(context.Request.Headers["Origin"]))
            {
                Reject(context, 403);
                return;
            }

            var wsContext = await context.AcceptWebSocketAsync(subProtocol: null);
            var socket = wsContext.WebSocket;
            ReplaceClient(socket);
            await ReceiveLoopAsync(socket);
        }
        catch (Exception)
        {
            // Network errors on one connection are not fatal for the server.
        }
    }

    private static void Reject(HttpListenerContext context, int statusCode)
    {
        context.Response.StatusCode = statusCode;
        context.Response.Close();
    }

    /// <summary>Makes <paramref name="socket"/> the active client and closes the previous one.</summary>
    private void ReplaceClient(WebSocket socket)
    {
        WebSocket? old;
        lock (_clientLock)
        {
            old = _client;
            _client = socket;
        }
        if (old is not null) _ = CloseQuietlyAsync(old);
    }

    private static async Task CloseQuietlyAsync(WebSocket socket)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            if (socket.State == WebSocketState.Open)
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "replaced", timeout.Token);
        }
        catch (Exception)
        {
            // Already gone; nothing to do.
        }
        finally
        {
            socket.Dispose();
        }
    }

    // ---- Receiving ----

    private async Task ReceiveLoopAsync(WebSocket socket)
    {
        try
        {
            while (socket.State == WebSocketState.Open && !_stop.IsCancellationRequested)
            {
                var text = await ReadMessageAsync(socket);
                if (text is null)
                {
                    await AnswerCloseAsync(socket);
                    break; // client closed
                }
                HandleText(text);
            }
        }
        catch (Exception)
        {
            // Connection dropped or was disposed: treated as a disconnect below.
        }
        finally
        {
            OnClientEnded(socket);
        }
    }

    /// <summary>Completes the close handshake so the client's CloseAsync returns cleanly.</summary>
    private static async Task AnswerCloseAsync(WebSocket socket)
    {
        try
        {
            if (socket.State == WebSocketState.CloseReceived)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", timeout.Token);
            }
        }
        catch (Exception)
        {
            // Client already gone.
        }
    }

    /// <summary>Reads one complete message; null when the client closed the socket. Binary messages give "".</summary>
    private async Task<string?> ReadMessageAsync(WebSocket socket)
    {
        var buffer = new byte[8192];
        using var message = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, _stop.Token);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            message.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        return result.MessageType == WebSocketMessageType.Text
            ? Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length)
            : string.Empty;
    }

    private void HandleText(string text)
    {
        var tabs = Protocol.ParseTabs(text);
        if (tabs is null) return; // not a tabs message: ignore
        try
        {
            TabsReceived?.Invoke(tabs);
        }
        catch (Exception)
        {
            // A faulty subscriber must not kill the receive loop.
        }
    }

    /// <summary>Called when a client's receive loop ends. Raises Disconnected only if it was still the active client.</summary>
    private void OnClientEnded(WebSocket socket)
    {
        bool wasActive;
        lock (_clientLock)
        {
            wasActive = ReferenceEquals(_client, socket);
            if (wasActive) _client = null;
        }
        if (!wasActive) return; // replaced by a newer client, or server disposed

        socket.Dispose();
        try
        {
            Disconnected?.Invoke();
        }
        catch (Exception)
        {
            // Ignore subscriber errors.
        }
    }

    // ---- Sending ----

    private async Task SendAsync(string json)
    {
        WebSocket? socket;
        lock (_clientLock) socket = _client;
        if (socket is null) return;

        await _sendLock.WaitAsync();
        try
        {
            if (socket.State != WebSocketState.Open) return;
            var bytes = Encoding.UTF8.GetBytes(json);
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, _stop.Token);
        }
        catch (Exception)
        {
            // The client went away while sending; the receive loop will report it.
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private Task SendCommandAsync(MediaSource s, string action, object? value) =>
        SendAsync(Protocol.BuildCommand(Protocol.TabIdOf(s), action, value));

    /// <inheritdoc />
    public Task TogglePlayAsync(MediaSource s) => SendCommandAsync(s, "toggle", null);
    /// <inheritdoc />
    public Task NextAsync(MediaSource s) => SendCommandAsync(s, "next", null);
    /// <inheritdoc />
    public Task PrevAsync(MediaSource s) => SendCommandAsync(s, "prev", null);
    /// <inheritdoc />
    public Task SeekAsync(MediaSource s, double seconds) => SendCommandAsync(s, "seek", seconds);
    /// <inheritdoc />
    public Task SetVolumeAsync(MediaSource s, double volume) => SendCommandAsync(s, "volume", volume);
    /// <inheritdoc />
    public Task SetLoopAsync(MediaSource s, LoopMode mode) => SendCommandAsync(s, "loop", mode);
    /// <inheritdoc />
    public Task JumpAsync(MediaSource s, int index) => SendCommandAsync(s, "jump", index);

    // ---- Cleanup ----

    /// <summary>Stops the listener and closes the client. Raises no events.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        WebSocket? client;
        lock (_clientLock)
        {
            client = _client;
            _client = null; // so the receive loop does not raise Disconnected
        }

        _stop.Cancel();
        try
        {
            _listener.Close();
        }
        catch (Exception)
        {
            // Already closed.
        }
        client?.Dispose();
    }
}
