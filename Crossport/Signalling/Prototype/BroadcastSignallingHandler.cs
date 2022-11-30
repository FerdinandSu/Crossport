using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Crossport.WebSockets;

namespace Crossport.Signalling.Prototype;

/// <summary>
/// 基础事件编号
///     不同的Broadcast应重写EventId来区分事件
/// </summary>
public enum SignallingEvents
{
    WsConnect=1,
    WsDisconnect=2,
    RtcConnect=3, 
    RtcDisconnect=4,
    RtcOffer=5,
    RtcAnswer=6,
    RtcCandidate=7,
    CpRegister=8
}
/// <summary>
/// 一个WebRTC广播域
/// </summary>
public class BroadcastSignallingHandler : ISignallingHandler, IBroadcastDomain
{
    private readonly ILogger<BroadcastSignallingHandler> _logger;

    public BroadcastSignallingHandler(ILogger<BroadcastSignallingHandler> logger)
    {
        _logger = logger;
    }

    private readonly ConcurrentDictionary<WebRtcPeer, HashSet<string>> _clients = new();
    private readonly ConcurrentDictionary<string, (WebRtcPeer?, WebRtcPeer?)> _connectionPairs = new();

    public bool IsEmpty => _clients.IsEmpty;

    protected virtual int EventId(SignallingEvents @event)=>(int)@event;
    public virtual void Add(WebRtcPeer session)
    {
        Track(session);
        _logger.LogDebug(EventId(SignallingEvents.WsConnect), "WebRtcSession {id} is now Online", session.Id);
    }

    public void Track(WebRtcPeer session)
    {
        _clients[session] = new();
        session.Answer += Answer;
        session.Connect += Connect;
        session.Offer += Offer;
        session.Candidate += Candidate;
        session.Disconnect += Disconnect;
    }
    public virtual async Task Remove(WebRtcPeer session)
    {
        await UnTrack(session);
        _logger.LogDebug(EventId(SignallingEvents.WsDisconnect), "WebRtcSession {id} is now Offline", session.Id);
    }

    public async Task UnTrack(WebRtcPeer session)
    {
        var connectionIds = _clients[session];
        foreach (var connectionId in connectionIds)
        {
            if (HasOppositePeer(session, connectionId, out var otherSessionWs) && otherSessionWs is not null)
            {
                try
                {
                    await otherSessionWs.SendAsync(new { type = "disconnect", connectionId });
                }
                catch (TaskCanceledException)
                {
                    // Ctrl+C pressed
                }
            }
            _connectionPairs.Remove(connectionId, out _);
        }

        _clients.Remove(session, out _);
        session.Answer -= Answer;
        session.Connect -= Connect;
        session.Offer -= Offer;
        session.Candidate -= Candidate;
        session.Disconnect -= Disconnect;
    }

    public async Task Connect(WebRtcPeer ws, string connectionId)
    {
        var polite = true;
        _logger.LogDebug(EventId(SignallingEvents.RtcConnect), "Connection {id} Introduced by {ws}.", connectionId, ws.Id);
        _connectionPairs[connectionId] = (ws, null);
        var connectionIds = _clients.GetOrAdd(ws, _ => new HashSet<string>());
        connectionIds.Add(connectionId);
        await ws.SendAsync(new { type = "connect", connectionId, polite });
        _logger.LogDebug(EventId(SignallingEvents.RtcConnect), "Public Connection {id} created by single peer {p0}.", connectionId, ws.Id);

    }

    protected bool HasOppositePeer(WebRtcPeer ws, string connectionId, out WebRtcPeer? another)
    {
        if (!_connectionPairs.TryGetValue(connectionId, out var p))
        {
            another = null;
            return false;
        }
        var (p0, p1) = p;
        another = p0 == ws ? p1 : p0;
        return true;
    }
    public async Task Disconnect(WebRtcPeer ws, string connectionId)
    {
        var connectionIds = _clients[ws];
        connectionIds.Remove(connectionId);

        if (!HasOppositePeer(ws, connectionId, out var otherSessionWs))
        {
            if (otherSessionWs is not null)
            {
                await otherSessionWs.SendAsync(
                    new { type = "disconnect", connectionId }
                );
            }
        }

        _connectionPairs.Remove(connectionId, out _);
        await ws.SendAsync(new { type = "disconnect", connectionId });
        _logger.LogInformation(EventId(SignallingEvents.RtcDisconnect), "Disconnect: {id}", connectionId);
    }
    public async Task<int> Broadcast<T>(T message, Predicate<WebRtcPeer> exceptBy)
    {
        var targets = _clients
            .Where(kv => exceptBy(kv.Key)).ToArray();
        await Task.WhenAll(targets
            .Select(kv =>
                kv.Key.SendAsync(message)));
        return targets.Length;
    }

    protected Task<int> Broadcast<T>(T message, WebRtcPeer? exception)
    {
        return Broadcast(message, p => p != exception);
    }
    public async Task Offer(WebRtcPeer sender, string from, string to, JsonElement data)
    {
        var message = data.DeserializeWeb<ISignallingHandler.OfferAnswerStruct>();
        if (message is null) return;
        var connectionId = message.ConnectionId;
        if (!_connectionPairs.ContainsKey(connectionId))
        {
            _logger.LogDebug(EventId(SignallingEvents.RtcOffer),
                "[Offer Ignored] Connection {id} offered by {sender}, but such connection doesn't exist, ignored.", connectionId, sender.Id);
            return;
        }
        var newOffer = new Offer(message.Sdp, DateTime.Now.ToJavascriptTimeStamp(), false);


        var targets=await Broadcast(new { from = connectionId, to = "", type = "offer", data = newOffer }, sender);

        _logger.LogDebug(EventId(SignallingEvents.RtcOffer), "[Offer] Offer on Connection {id} provided by {p0} Broadcast to {n} peers.", connectionId, sender.Id, targets);



    }

    public async Task Answer(WebRtcPeer sender, string from, string to, JsonElement data)
    {
        var message = data.DeserializeWeb<ISignallingHandler.OfferAnswerStruct>();
        if (message is null) return;
        var connectionId = message.ConnectionId;
        if (!_connectionPairs.ContainsKey(connectionId))
        {
            _logger.LogDebug(EventId(SignallingEvents.RtcAnswer),
                "[Answer Ignored] Connection {id} answered by {sender}, but failed to get another peer, ignored.", connectionId, sender.Id);
            return;
        }
        var connectionIds = _clients.GetOrAdd(sender, _ => new HashSet<string>());
        connectionIds.Add(connectionId);

        if (!HasOppositePeer(sender, connectionId, out var otherSessionWs) || otherSessionWs is null)
        {
            _logger.LogWarning(
                "[Answer Ignored] Connection {id} answered by {sender}, but failed to get another peer, ignored.", connectionId, sender.Id);
            return;
        }

        await otherSessionWs.SendAsync(new
        {
            from = connectionId,
            to = "",
            type = "answer",
            data = new Answer(message.Sdp, DateTime.Now.ToJavascriptTimeStamp())
        });
        _logger.LogInformation(EventId(SignallingEvents.RtcAnswer), "[Answer] Connection {id} (introduced by {osw}) answered by {sender}; Connection established.", connectionId, otherSessionWs?.Id, sender.Id);

        _connectionPairs[connectionId] = (otherSessionWs, sender);

    }
    public async Task Candidate(WebRtcPeer sender, string from, string to, JsonElement data)
    {
        var message = data.DeserializeWeb<ISignallingHandler.CandidateStruct>();
        if (message is null) return;
        var connectionId = message.ConnectionId;
        if (!_connectionPairs.ContainsKey(connectionId))
        {
            _logger.LogDebug(EventId(SignallingEvents.RtcCandidate),
                "[Candidate Ignored] Connection {id} not exist, ignored.", connectionId);
            return;
        }


        var targets=await Broadcast(new
        {
            from = connectionId,
            to = "",
            type = "candidate",
            data = new CandidateRecord(message.Candidate, message.SdpMLineIndex, message.SdpMid.ToString(), DateTime.Now.ToJavascriptTimeStamp())
        }, sender);
        _logger.LogDebug(EventId(SignallingEvents.RtcCandidate), "[Candidate] Candidate on Connection {id} provided by {p0} Broadcast to {n} peers.", connectionId, sender.Id, targets);
    }
}