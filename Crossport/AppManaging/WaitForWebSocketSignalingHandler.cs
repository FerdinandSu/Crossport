using Crossport.Signalling;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading;

namespace Crossport.AppManaging;

public class WaitForWebSocketSignalingHandler : WebSocketSignalingHandler
{
    public record WaitFor(Func<Dictionary<string, object>, bool> Predict, SignalingMessageHandler Handler, CancellationToken WaitForTimer);

    private readonly Queue<Dictionary<string, object>> _messageQueue;
    private bool _waited = false;
    private readonly WaitFor _waitFor;

    public override event SignalingDisconnectHandler? OnDisconnect;
    public override event SignalingMessageHandler? OnMessage;

    public WaitForWebSocketSignalingHandler(
        WebSocket socket, WaitFor waitFor,
        TaskCompletionSource completionSource,
        CancellationToken cancellationToken):base(socket,completionSource,cancellationToken)
    {
        _waitFor = waitFor;
        _messageQueue = new();
    }

    protected override async Task ReceiveResponse(Dictionary<string, object> message)
    {
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

    }

}