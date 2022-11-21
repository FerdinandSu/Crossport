using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Crossport.WebSockets;

public class WebRtcSession
{
    private readonly WebSocket _webSocket;
    private readonly CancellationToken _cancellationToken;
    private readonly TaskCompletionSource _completionSource;

    public delegate Task ConnectEvent(WebRtcSession sender, string connectionId);
    public delegate Task ExchangeEvent(WebRtcSession sender, string from, string to, JsonElement data);

    public event ConnectEvent? Connect;
    public event ConnectEvent? Disconnect;
    public event ExchangeEvent? Offer;
    public event ExchangeEvent? Answer;
    public event ExchangeEvent? Candidate;
    public Guid Id { get; } = Guid.NewGuid();
    public WebRtcSession(WebSocket socket, CancellationToken cancellationToken, TaskCompletionSource completionSource)
    {

        _webSocket = socket;
        _cancellationToken = cancellationToken;
        _completionSource = completionSource;
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

    private async Task ReceiveLoop()
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
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                        return;
                    }

                }
                while (!receiveResult.EndOfMessage);

                if (receiveResult.MessageType == WebSocketMessageType.Close)
                {
                    _completionSource.SetResult();
                    return;

                }
                outputStream.Position = 0;

                await ReceiveResponse(outputStream);
            }
        }
        catch (TaskCanceledException) { }
        await DisconnectAsync();// 主动断开
        _completionSource.SetResult();
    }

    public async Task SendAsync<T>(T message)
    {
        var token = _cancellationToken;
        await using var outputStream = new MemoryStream(ReceiveBufferSize);
        await JsonSerializer.SerializeAsync(outputStream, message, new JsonSerializerOptions(JsonSerializerDefaults.Web),
            token);
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
            var byteCountRead = await outputStream.ReadAsync(readBuffer, 0, ReceiveBufferSize, token);
            var atTail = outputStream.Position == outputStream.Length;
            await _webSocket.SendAsync(writeBuffer[..byteCountRead], WebSocketMessageType.Text,atTail,
                token);
            if (atTail) break;
        }
    }

    private static string ExportJsonString(object? obj) => obj?.ToString() ?? "";
    private async Task ReceiveResponse(Stream inputStream)
    {

        var message = await JsonSerializer.DeserializeAsync<Dictionary<string, object>>(inputStream, new JsonSerializerOptions(JsonSerializerDefaults.Web), _cancellationToken);
        if (message is null) return;
        var type = message.SafeGetString("type").ToLower();
        switch (type)
        {
            case "connect":
                await (Connect?.Invoke(this, message.SafeGetString("connectionId")) ?? Task.CompletedTask);
                break;
            case "disconnect":
                await (Disconnect?.Invoke(this, message.SafeGetString("connectionId")) ?? Task.CompletedTask);
                break;
            case "offer":
                await (Offer?.Invoke(this, message.SafeGetString("from"), message.SafeGetString("to"), (JsonElement)message["data"]) ?? Task.CompletedTask);
                break;
            case "answer":
                await (Answer?.Invoke(this, message.SafeGetString("from"), message.SafeGetString("to"), (JsonElement)message["data"]) ?? Task.CompletedTask);
                break;
            case "candidate":
                await (Candidate?.Invoke(this, message.SafeGetString("from"), message.SafeGetString("to"), (JsonElement)message["data"]) ?? Task.CompletedTask);
                break;
            default:
                throw new ArgumentException($"Type {type} is not supported by signalling.", nameof(type));
        }
    }
}