using System.Collections.Concurrent;
using System.Text.Json;
using Crossport.WebSockets;

namespace Crossport.Signalling.Prototype;

public class SingleSignallingHandler : ISignallingHandler
{
    private readonly ILogger<SingleSignallingHandler> _logger;

    public SingleSignallingHandler(ILogger<SingleSignallingHandler> logger)
    {
        _logger = logger;
    }

    private readonly ConcurrentDictionary<WebRtcPeer, HashSet<string>> _clients = new();
    private readonly ConcurrentDictionary<string, (WebRtcPeer?, WebRtcPeer?)> _connectionPairs = new();

    //public bool IsPrivate { get; set; } = false;
    public ISet<string> GetOrCreateConnectionIds(WebRtcPeer session)
    {
        if (_clients.ContainsKey(session)) return _clients[session];
        var connectionIds = new HashSet<string>();
        _clients[session] = connectionIds;
        return connectionIds;
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

    public async Task UnTrack(WebRtcPeer session)
    {
        var connectionIds = _clients[session];
        foreach (var connectionId in connectionIds)
        {
            var (p0, p1) = _connectionPairs.TryGetValue(connectionId, out var p) ? p : (null, null);
            if (p0 is not null)
            {
                var otherSessionWs = p0 == session ? p0 : p1;
                if (otherSessionWs is not null)
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
            }

            _connectionPairs.Remove(connectionId, out _);
        }


        _clients.Remove(session, out _);
    }

    public void Add(WebRtcPeer session)
    {
        Track(session);
        _logger.LogDebug("WebRtcSession {id} is now Online", session.Id);
    }

    public async Task Remove(WebRtcPeer session)
    {
        await UnTrack(session);
        _logger.LogDebug("WebRtcSession {id} is now Offline", session.Id);
    }

    private async Task Connect(WebRtcPeer ws, string connectionId)
    {
        var polite = true;
        _logger.LogDebug("Connection {id} Introduced by {ws}.", connectionId, ws.Id);

        if (_connectionPairs.ContainsKey(connectionId))
        {
            var (p0, p1) = _connectionPairs[connectionId];

            if (p0 is not null && p1 is not null)
            {
                await ws.SendAsync(
                    new
                    {
                        type = "error",
                        message = $"{connectionId}: This connection id is already used."
                    });
                _logger.LogDebug("Connection {id} Introduced by {ws} Failed: Duplicated Id.", connectionId, ws.Id);
                return;
            }
            if (p1 is null)
            {
                _connectionPairs[connectionId] = (p0, ws);
                _logger.LogDebug("Connection {id} Established by peers {p0} and {p1}.", connectionId, p0.Id, ws.Id);
            }
        }
        else
        {
            _connectionPairs[connectionId] = (ws, null);
            _logger.LogDebug("Connection {id} created by single peer {p0}.", connectionId, ws.Id);
            polite = false;
        }

        var connectionIds = GetOrCreateConnectionIds(ws);
        connectionIds.Add(connectionId);
        await ws.SendAsync(new { type = "connect", connectionId, polite });

    }

    private async Task Disconnect(WebRtcPeer ws, string connectionId)
    {
        var connectionIds = _clients[ws];
        connectionIds.Remove(connectionId);

        if (_connectionPairs.ContainsKey(connectionId))
        {
            var (p0, p1) = _connectionPairs[connectionId];
            var otherSessionWs = p0 == ws ? p0 : p1;
            if (otherSessionWs is not null)
            {
                await otherSessionWs.SendAsync(
                    new { type = "disconnect", connectionId }
                );
            }
        }

        _connectionPairs.Remove(connectionId, out _);
        await ws.SendAsync(new { type = "disconnect", connectionId });
        _logger.LogInformation("Disconnect: {id}", connectionId);
    }

    private async Task Offer(WebRtcPeer sender, string from, string to, JsonElement data)
    {
        var message = data.Deserialize<ISignallingHandler.OfferAnswerStruct>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (message is null) return;
        var connectionId = message.ConnectionId;
        var newOffer = new Offer(message.Sdp, DateTime.Now.ToJavascriptTimeStamp(), true);
        if (!_connectionPairs.ContainsKey(connectionId)) return;
        var (p0, p1) = _connectionPairs[connectionId];
        var otherSessionWs = p0 == sender ? p1 : p0;
        if (otherSessionWs is null) return;
        await otherSessionWs.SendAsync(new
        {
            from = connectionId,
            to = "",
            type = "offer",
            data = newOffer
        });
        _logger.LogDebug("[Offer] Offer on Connection {id} provided by {p0} sent to {p1}.", connectionId, sender.Id, otherSessionWs.Id);


    }

    private async Task Answer(WebRtcPeer sender, string from, string to, JsonElement data)
    {
        var message = data.Deserialize<ISignallingHandler.OfferAnswerStruct>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (message is null) return;
        var connectionId = message.ConnectionId;
        var connectionIds = GetOrCreateConnectionIds(sender);
        connectionIds.Add(connectionId);
        var newAnswer = new Answer(message.Sdp, DateTime.Now.ToJavascriptTimeStamp());

        if (!_connectionPairs.ContainsKey(connectionId)) return;

        var (p0, p1) = _connectionPairs[connectionId];
        var otherSessionWs = p0 == sender ? p1 : p0;


        if (otherSessionWs is not null)
            await otherSessionWs.SendAsync(new
            {
                from = connectionId,
                to = "",
                type = "answer",
                data = newAnswer
            });
        _logger.LogInformation("[Answer] Connection {id} (introduced by {osw}) answered by {sender}; Connection established.", connectionId, otherSessionWs.Id, sender.Id);
    }

    private async Task Candidate(WebRtcPeer sender, string from, string to, JsonElement data)
    {
        var message = data.Deserialize<ISignallingHandler.CandidateStruct>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (message is null) return;
        var connectionId = message.ConnectionId;
        var candidate = new CandidateRecord(message.Candidate, message.SdpMLineIndex, message.SdpMid.ToString(), DateTime.Now.ToJavascriptTimeStamp());
        if (_connectionPairs.ContainsKey(connectionId))
        {
            var (p0, p1) = _connectionPairs[connectionId];
            var otherSessionWs = p0 == sender ? p1 : p0;
            if (otherSessionWs is not null)
            {
                await otherSessionWs.SendAsync(new
                {
                    from = connectionId,
                    to = "",
                    type = "candidate",
                    data = candidate
                });
                _logger.LogDebug("[Candidate] Candidate on Connection {id} provided by {p0} sent to {p1}.", connectionId, sender.Id, otherSessionWs.Id);
            }
        }
    }
}