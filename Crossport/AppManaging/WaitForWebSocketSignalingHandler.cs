using Crossport.Signalling;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;

namespace Crossport.AppManaging;

public class WaitForWebSocketSignalingHandler : IDisposable, ISignalingHandler
{
    public record WaitFor(Func<Dictionary<string, object>, bool> Predict, SignalingMessageHandler Handler, CancellationToken WaitForTimer);
    private readonly WebSocket _webSocket;
    private readonly CancellationToken _cancellationToken;
    private readonly TaskCompletionSource _completionSource;
    private readonly Queue<Dictionary<string, object>> _messageQueue;
    private bool _waited = false;
    private readonly WaitFor _waitFor;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public event SignalingDisconnectHandler? OnDisconnect;
    public event SignalingMessageHandler? OnMessage;

    public Guid Id { get; } = Guid.NewGuid();
    public WaitForWebSocketSignalingHandler(
        WebSocket socket, WaitFor waitFor,
        TaskCompletionSource completionSource,
        CancellationToken cancellationToken)
    {
        _webSocket = socket;
        _waitFor = waitFor;
        _cancellationToken = cancellationToken;
        _completionSource = completionSource;
        _messageQueue = new();
    }
    private const int ReceiveBufferSize = 8192;
    public Task ListenAsync() => Task.Run(ReceiveLoop, _cancellationToken);


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

    public async Task SendAsync<T>(T message)
    {
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
    }

    protected virtual async Task ReceiveResponse(Dictionary<string, object> message)
    {
        await _semaphore.WaitAsync(_cancellationToken);
        if (_waited)
        {
            await (OnMessage?.Invoke(this, message) ?? Task.CompletedTask);
        }
        else
        {
            var (predict, handler, token) = _waitFor;
            if (token.IsCancellationRequested)
            {
                // WaitFor Timeout
                await DisconnectAsync();
                return;
            }
            if (predict(message))
            {
                await handler(this, message);
                foreach (var previousMessage in _messageQueue)
                {
                    await (OnMessage?.Invoke(this, previousMessage) ?? Task.CompletedTask);
                }
                _waited = true;
            }
            else
            {
                _messageQueue.Enqueue(message);
            }
        }
        _semaphore.Release();

    }
    private async Task ReceiveResponse(Stream inputStream)
    {
        var message = await JsonSerializer.DeserializeAsync<Dictionary<string, object>>(inputStream, new JsonSerializerOptions(JsonSerializerDefaults.Web), _cancellationToken);
        if (message is null) return;
        await ReceiveResponse(message);
    }

    public void Dispose()
    {
        _semaphore.Dispose();
        _webSocket.Dispose();
    }
}