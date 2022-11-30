using System.Collections.Concurrent;
using System.Text.Json;
using Crossport.Signalling.Prototype;
using Crossport.WebSockets;

namespace Crossport.Signalling;
public class CrossportSignallingHandler : BroadcastSignallingHandler, ISignallingHandler, IBroadcastDomain
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<BroadcastSignallingHandler> _logger;
    private readonly ConcurrentDictionary<string, CrossportPeer> _crossportClients = new();
    //private readonly ConcurrentDictionary<WebRtcPeer, HashSet<string>> _clients = new();
    //private readonly ConcurrentDictionary<string, (WebRtcPeer?, WebRtcPeer?)> _connectionPairs = new();
    public CrossportSignallingHandler(ILoggerFactory loggerFactory, ILogger<BroadcastSignallingHandler> logger) : base(logger)
    {
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    private readonly ConcurrentDictionary<string, ApplicationSignallingHandler> _apps = new();

    public override void Add(WebRtcPeer session)
    {
        Track(session);
        if (session is CrossportPeer cp)
            cp.Register += Register;
        _logger.LogDebug(EventId(SignallingEvents.WsConnect), "Anonymous WebRtcSession {id} is now Online", session.Id);
    }

    protected async Task Register(CrossportPeer sender)
    {
        var appName = sender.Config?.Application;
        var clientId = sender.ClientId;
        if (sender.Config is null || string.IsNullOrEmpty(appName))
        {
            _logger.LogWarning(EventId(SignallingEvents.CpRegister), "Empty Application name, from peer {peer}", sender.Id);
            return;
        }
        if (string.IsNullOrEmpty(clientId))
        {
            _logger.LogWarning(EventId(SignallingEvents.CpRegister), "Empty Client Id, from peer {peer}", sender.Id);
            return;
        }

        var originPeer = _crossportClients.GetValueOrDefault(clientId);
        var componentName = sender.Config.Component;
        if (originPeer == sender && originPeer.Config == sender.Config) return;

        if (originPeer == null)
        {
            // 是否允许匿名域
            if (!sender.Config.AllowAnonymous) await UnTrack(sender);
            var appHandler = _apps.GetOrAdd(appName, _ => new ApplicationSignallingHandler(_loggerFactory));
            appHandler.Track(sender);
            _logger.LogDebug(EventId(SignallingEvents.CpRegister), "New {mode} Crossport Session {id} ({app}/{component}/{cid}) is Registered.", sender.Mode, sender.Id, appName,
                componentName, clientId);
        }
        else
        {
            // Reset Connection
            var oldHandler = _apps[originPeer.Config!.Application];
            var appHandler = _apps.GetOrAdd(appName, _ => new ApplicationSignallingHandler(_loggerFactory));
            await oldHandler.UnTrack(originPeer);
            if (originPeer != sender || (originPeer.Config.AllowAnonymous && !sender.Config.AllowAnonymous))
                await UnTrack(sender);
            if (!originPeer.Config.AllowAnonymous && sender.Config!.AllowAnonymous)
                Track(sender);
            appHandler.Track(sender);
            _logger.LogDebug(EventId(SignallingEvents.CpRegister),"Crossport {mode} Session {id} ({app}/{component}/{cid}) Overrides {mode} Session {oldId} ({app}/{component}/{cid}).", sender.Id, appName,
                 sender.Mode, componentName, clientId, originPeer.Id, originPeer.Mode, originPeer.Config.Application, originPeer.Config.Component, originPeer.ClientId);
        }

        _crossportClients.AddOrUpdate(clientId, sender, (_, _) => sender);



    }

    private static bool IsAnonymous(WebRtcPeer session, out string appName)
    {
        var cp = session as CrossportPeer;
        appName = cp?.Config?.Application ?? string.Empty;
        return string.IsNullOrEmpty(appName);//空的应用名也被视为匿名
    }

    public override async Task Remove(WebRtcPeer session)
    {
        if (IsAnonymous(session, out var app))
        {
            await UnTrack(session);
            _logger.LogDebug(EventId(SignallingEvents.WsDisconnect),"Anonymous WebRtcSession {id} is now Offline", session.Id);
        }
        else
        {
            await _apps[app].UnTrack(session);
            var cp = session as CrossportPeer;
            var componentName = cp?.Config?.Component ?? string.Empty;
            var clientId = cp?.ClientId ?? string.Empty;
            _crossportClients.Remove(clientId, out _);
            _logger.LogDebug(EventId(SignallingEvents.WsDisconnect),"Crossport Session {id} ({app}/{component}/{cid}) is now Offline", session.Id, app,
                componentName, clientId);
        }
    }
    // [Connect], [Disconnect], [Offer], [Answer], [Candidate] handles only requests from Anonymous peers, so NOT override.
    protected override int EventId(SignallingEvents @event)
    {
        return 10 + base.EventId(@event);
    }
}