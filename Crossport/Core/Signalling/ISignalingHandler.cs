namespace Crossport.Core.Signalling;

public delegate Task SignalingMessageHandler(ISignalingHandler sender, Dictionary<string, object> message);

public delegate Task SignalingDisconnectHandler(ISignalingHandler sender);

public interface ISignalingHandler
{
    event SignalingDisconnectHandler OnDisconnect;
    event SignalingMessageHandler? OnMessage;
    Task<bool> SendAsync<T>(T message);
    Task DisconnectAsync();
}