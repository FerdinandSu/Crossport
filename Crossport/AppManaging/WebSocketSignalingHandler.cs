using System.Net.WebSockets;
using System.Text.Json;

namespace Crossport.AppManaging;

public class WebSocketSignalingHandler : IDisposable, ISignalingHandler
{
    private const int ReceiveBufferSize = 8192;
    private readonly WebSocket _webSocket;
    private readonly CancellationToken _cancellationToken;
    private readonly TaskCompletionSource _completionSource;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    public event SignalingDisconnectHandler? OnDisconnect;
    public virtual event SignalingMessageHandler? OnMessage;
    public Task ListenAsync() => Task.Run((Func<Task?>)ReceiveLoop, _cancellationToken);

    public WebSocketSignalingHandler(
        WebSocket socket, 
        TaskCompletionSource completionSource,
        CancellationToken cancellationToken)
    {
        _webSocket = socket;
        
        _cancellationToken = cancellationToken;
        _completionSource = completionSource;
        
    }

    public async Task DisconnectAsync()
    {

        // TODO: requests cleanup code, sub-protocol dependent.
        if (_webSocket.State == WebSocketState.Open)
        {
            await _webSocket.CloseOutputAsync(WebSocketCloseStatus.Empty, "", CancellationToken.None);
            await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        }
    }

    private async Task ReceiveLoopInternal()
    {
        var buffer = new byte[ReceiveBufferSize];
        try
        {
            while (!_cancellationToken.IsCancellationRequested)
            {
                if (_webSocket.State != WebSocketState.Open) return;
                await using var outputStream = new MemoryStream(ReceiveBufferSize);
                WebSocketReceiveResult receiveResult;
                do
                {
                    try
                    {
                        receiveResult = await _webSocket.ReceiveAsync(buffer, _cancellationToken);
                        if (receiveResult.MessageType != WebSocketMessageType.Close)
                            outputStream.Write(buffer, 0, receiveResult.Count);
                    }
                    catch (WebSocketException)
                    {
                        // Exit without handshake
                        return;
                    }

                }
                while (!receiveResult.EndOfMessage);

                if (receiveResult.MessageType == WebSocketMessageType.Close) return;
                outputStream.Position = 0;

                await ReceiveResponse(outputStream);
            }
        }
        catch (TaskCanceledException) { }
        await DisconnectAsync();// 主动断开
    }

    private async Task ReceiveLoop()
    {
        await ReceiveLoopInternal();
        await (OnDisconnect?.Invoke(this) ?? Task.CompletedTask);
        _completionSource.SetResult();
    }

    public async Task<bool> SendAsync<T>(T message)
    {
        if (_webSocket.State != WebSocketState.Open) return false;
        await using var outputStream = new MemoryStream(ReceiveBufferSize);
        await JsonSerializer.SerializeAsync(outputStream, message, new JsonSerializerOptions(JsonSerializerDefaults.Web),
            _cancellationToken);
        var readBuffer = new byte[ReceiveBufferSize];
        var writeBuffer = new ArraySegment<byte>(readBuffer);
        if (outputStream.Length > ReceiveBufferSize)
        {
            outputStream.Position = 0;
            var text = await new StreamReader(outputStream).ReadToEndAsync();
            if (text.EndsWith(">"))
            {
                Console.WriteLine(text);
                Console.WriteLine();
            }
        }
        outputStream.Position = 0;
        for (; ; )
        {
            var byteCountRead = await outputStream.ReadAsync(readBuffer, 0, ReceiveBufferSize, _cancellationToken);
            var atTail = outputStream.Position == outputStream.Length;
            await _webSocket.SendAsync(writeBuffer[..byteCountRead], WebSocketMessageType.Text, atTail,
                _cancellationToken);
            if (atTail) break;
        }
        return true;
    }

    protected virtual async Task ReceiveResponse(Dictionary<string, object> message)
    {
        await (OnMessage?.Invoke(this, message) ?? Task.CompletedTask);

    }

    private async Task ReceiveResponse(Stream inputStream)
    {
        var message = await JsonSerializer.DeserializeAsync<Dictionary<string, object>>(inputStream, new JsonSerializerOptions(JsonSerializerDefaults.Web), _cancellationToken);
        if (message is null) return;
        await _semaphore.WaitAsync(_cancellationToken);
        await ReceiveResponse(message);
        _semaphore.Release();
    }

    public void Dispose()
    {
        _semaphore.Dispose();
        _webSocket.Dispose();
    }
}