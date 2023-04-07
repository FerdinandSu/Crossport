using Crossport.RtcEntities;
using Crossport.Signalling;
using System;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Runtime.Serialization;
using System.Text.Json;

namespace Crossport.AppManaging;
public delegate Task ConnectEvent(Peer sender, string connectionId);
public delegate Task ExchangeEvent(Peer sender, string from, string to, JsonElement data);
public enum PeerRole
{
    ContentConsumer = 0, ContentProvider = 1
}

public enum PeerStatus
{
    // Raw实际上不存在于任何Peer里，但是习惯上还是把它放在了这里
    Raw = 0,
    Standard = 1,
    Compatible = 2,
    Lost = 3,
    Dead = 5
}
/// <summary>
/// Reconnect to a alive peer
/// </summary>
[Serializable]
public class ReconnectAlivePeerException : Exception
{
    //
    // For guidelines regarding the creation of new exception types, see
    //    http://msdn.microsoft.com/library/default.asp?url=/library/en-us/cpgenref/html/cpconerrorraisinghandlingguidelines.asp
    // and
    //    http://msdn.microsoft.com/library/default.asp?url=/library/en-us/dncscol/html/csharp07192001.asp
    //

    public ReconnectAlivePeerException()
    {
    }

    public ReconnectAlivePeerException(string message) : base(message)
    {
    }

    public ReconnectAlivePeerException(string message, Exception inner) : base(message, inner)
    {
    }

    protected ReconnectAlivePeerException(
        SerializationInfo info,
        StreamingContext context) : base(info, context)
    {
    }
}
public abstract class Peer
{
    public static int LostPeerLifetime = 5000;
    // To keep the send order.
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly Queue<object> _sendQueue = new();
    private bool _manualShutdown=false;
    public static bool operator ==(Peer? left, Peer? right) => Equals(left, right);
    public static bool operator !=(Peer? left, Peer? right) => !Equals(left, right);
    protected bool Equals(Peer other) => Id.Equals(other.Id);
    public event Action<Peer>? OnPeerDead;
    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        return obj.GetType() == GetType() && Equals((Peer)obj);
    }

    public override int GetHashCode()
    {
        return Id.GetHashCode();
    }

    protected Peer(ISignalingHandler signaling, Guid id, CrossportConfig config, bool isCompatible)
    {
        Id = id;
        Signaling = signaling;
        Status = isCompatible ? PeerStatus.Compatible : PeerStatus.Standard;
        Role = config.Character == 0 ? PeerRole.ContentConsumer : PeerRole.ContentProvider;
        RegisterEvents();
    }

    public async Task SendAsync(object message)
    {
        await _sendLock.WaitAsync();
        while (!_sendQueue.Any())
        {
            var oldMessage = _sendQueue.Peek();
            if (await Signaling.SendAsync(oldMessage))
            {
                _sendQueue.Dequeue();
            }
        }

        if (!await Signaling.SendAsync(message))
        {
            _sendQueue.Enqueue(message);
        }
        _sendLock.Release();
    }

    public event ConnectEvent? OnConnect;
    public event ConnectEvent? OnDisconnect;
    public event ExchangeEvent? OnOffer;
    public event ExchangeEvent? OnAnswer;
    public event ExchangeEvent? OnCandidate;
    private void UnRegisterEvents()
    {
        Signaling.OnMessage -= Signaling_OnMessage;
        Signaling.OnDisconnect -= Signaling_OnDisconnect;
    }
    private void RegisterEvents()
    {
        Signaling.OnMessage += Signaling_OnMessage;
        Signaling.OnDisconnect += Signaling_OnDisconnect;
    }

    private async Task Signaling_OnMessage(ISignalingHandler sender, Dictionary<string, object> message)
    {
        var type = message.SafeGetString("type").ToLower();
        switch (type)
        {
            case "connect":
                await (OnConnect?.Invoke(this, message.SafeGetString("connectionId")) ?? Task.CompletedTask);
                break;
            case "disconnect":
                await (OnDisconnect?.Invoke(this, message.SafeGetString("connectionId")) ?? Task.CompletedTask);
                break;
            case "offer":
                await (OnOffer?.Invoke(this, message.SafeGetString("from"), message.SafeGetString("to"), (JsonElement)message["data"]) ?? Task.CompletedTask);
                break;
            case "answer":
                await (OnAnswer?.Invoke(this, message.SafeGetString("from"), message.SafeGetString("to"), (JsonElement)message["data"]) ?? Task.CompletedTask);
                break;
            case "candidate":
                await (OnCandidate?.Invoke(this, message.SafeGetString("from"), message.SafeGetString("to"), (JsonElement)message["data"]) ?? Task.CompletedTask);
                break;
            default:
                throw new ArgumentException($"Type {type} is not supported by Crossport Peer.", nameof(type));
        }
    }

    private Task Signaling_OnDisconnect(ISignalingHandler sender)
    {
        Status = PeerStatus.Lost;
        UnRegisterEvents();
        if (_manualShutdown)
        {
            ShutdownInternal();
            return Task.CompletedTask;
        }
        
        // Allow WebRtc Peer to stay alive for some time, as reconnecting is allowed
        return Task.Delay(LostPeerLifetime).ContinueWith(_ => ShutdownInternal());
    }

    private void ShutdownInternal()
    {
        if (Status != PeerStatus.Lost) return;
        Status = PeerStatus.Dead;
        OnPeerDead?.Invoke(this);
        Dispose();
    }
    public async Task Shutdown()
    {
        _manualShutdown= true;
        await Signaling.DisconnectAsync();
    }
    public void Reconnect(ISignalingHandler signaling, bool isCompatible)
    {
        if (Status != PeerStatus.Lost)
        {
            throw new ReconnectAlivePeerException($"Peer {Id}({Role}) is still alive and shouldn't be reconnected.");
        }
        Signaling = signaling;
        Status = isCompatible ? PeerStatus.Compatible : PeerStatus.Standard;
        RegisterEvents();
    }
    protected ISignalingHandler Signaling { get; private set; }
    public Guid Id { get; }
    public PeerStatus Status { get; private set; }
    public PeerRole Role { get; }

    
    protected void Dispose()
    {
        // Internally called when shutdown (Dead)
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _sendLock.Dispose();
        }
    }
}