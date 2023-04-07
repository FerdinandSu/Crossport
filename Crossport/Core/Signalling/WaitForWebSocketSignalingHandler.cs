using System.Net.WebSockets;

namespace Crossport.Core.Signalling;

public class WaitForWebSocketSignalingHandler : WebSocketSignalingHandler
{
    private readonly Queue<Dictionary<string, object>> _messageQueue;
    private readonly WaitFor _waitFor;
    private bool _waited;

    public WaitForWebSocketSignalingHandler(
        WebSocket socket, WaitFor waitFor,
        TaskCompletionSource completionSource,
        CancellationToken cancellationToken) : base(socket, completionSource, cancellationToken)
    {
        _waitFor = waitFor;
        _messageQueue = new Queue<Dictionary<string, object>>();
    }

    //public override event SignalingDisconnectHandler? OnDisconnect;
    public override event SignalingMessageHandler? OnMessage;

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
                    await (OnMessage?.Invoke(this, previousMessage) ?? Task.CompletedTask);
                _waited = true;
            }
            else
            {
                _messageQueue.Enqueue(message);
            }
        }
    }

    public record WaitFor(Func<Dictionary<string, object>, bool> Predict, SignalingMessageHandler Handler,
        CancellationToken WaitForTimer);
}