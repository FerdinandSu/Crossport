using Crossport.Signalling.Prototype;
using System.Collections.Concurrent;

namespace Crossport.Signalling;

public class ComponentSignallingHandler : BroadcastSignallingHandler
{
    public ComponentSignallingHandler(ILogger<BroadcastSignallingHandler> logger) : base(logger)
    {
    }
    protected override int EventId(SignallingEvents @event)
    {
        return 20+base.EventId(@event);
    }
}

public class ApplicationSignallingHandler : ISignallingHandler,IBroadcastDomain
{
    private readonly ILoggerFactory _loggerFactory;
    public ApplicationSignallingHandler(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }
    private readonly ConcurrentDictionary<string, ComponentSignallingHandler> _components = new();
    public void Track(WebRtcPeer session)
    {
        if (session is not CrossportPeer cp || cp.Config is null)
            throw new NotSupportedException(
                $"{nameof(ApplicationSignallingHandler)} cannot handle peers as type {session.GetType().FullName}, or session is not registered.");
        var broadcast = _components.GetOrAdd(cp.Config.Component,
            s => new ComponentSignallingHandler(_loggerFactory.CreateLogger<ComponentSignallingHandler>()));
        broadcast.Track(session);
    }

    public async Task UnTrack(WebRtcPeer session)
    {
        if (session is not CrossportPeer cp || cp.Config is null)
            throw new NotSupportedException(
                $"{nameof(ApplicationSignallingHandler)} cannot handle peers as type {session.GetType().FullName}, or session is not registered.");
        if (_components.TryGetValue(cp.Config.Component, out var broadcast))
        {
            await broadcast.UnTrack(session);
            if (broadcast.IsEmpty) _components.Remove(cp.Config.Component, out _);
        }
    }

    public void Add(WebRtcPeer session)
    {
        Track(session);

    }

    public async Task<int> Broadcast<T>(T message, Predicate<WebRtcPeer> exceptBy)
    {
        return (await Task.WhenAll(_components.Select(kv=>kv.Value.Broadcast(message, exceptBy)))).Sum();
    }
    public async Task Remove(WebRtcPeer session)
    {
        await UnTrack(session);

    }
}