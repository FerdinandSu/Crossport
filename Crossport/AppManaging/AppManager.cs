using System.Collections.Concurrent;
using System.Runtime.Serialization;
using System.Text.Json;

namespace Crossport.AppManaging;

[Serializable]
public class BadRegisterException : Exception
{
    //
    // For guidelines regarding the creation of new exception types, see
    //    http://msdn.microsoft.com/library/default.asp?url=/library/en-us/cpgenref/html/cpconerrorraisinghandlingguidelines.asp
    // and
    //    http://msdn.microsoft.com/library/default.asp?url=/library/en-us/dncscol/html/csharp07192001.asp
    //

    public BadRegisterException()
    {
    }

    public BadRegisterException(string message) : base(message)
    {
    }

    public BadRegisterException(string message, Exception inner) : base(message, inner)
    {
    }

    protected BadRegisterException(
        SerializationInfo info,
        StreamingContext context) : base(info, context)
    {
    }
}
public record AppInfo(string Application, string Component)
{
    public AppInfo(CrossportConfig crossportConfig) : this(crossportConfig.Application, crossportConfig.Component) { }
}
public class AppManager
{

    private readonly ILogger<AppManager> _logger;

    public AppManager(ILogger<AppManager> logger)
    {
        _logger = logger;
    }
    private ConcurrentDictionary<AppInfo, AppComponent> AppComponents { get; } = new();
    private ConcurrentDictionary<Guid, Peer> Peers { get; } = new();
    public async Task RegisterOrRenew(ISignalingHandler signaling, Dictionary<string, object> message, bool isCompatible)
    {
        var peerId = Guid.Parse(message.SafeGetString("id"));
        if (Peers.TryGetValue(peerId, out var peer))
        {
            peer.Reconnect(signaling, isCompatible);
            return;
        }
        var config = ((JsonElement)message["data"]).DeserializeWeb<CrossportConfig>();
        if (config is not null)
        {
            var app = AppComponents.GetOrAdd(new AppInfo(config), i => new AppComponent(i));
            if (config.Character == 0)
            {
                var consumer = new ContentConsumer(signaling, peerId, config, isCompatible);
                app.Register(consumer);
                Peers[peerId] = consumer;
            }
            else
            {
                var provider = new ContentProvider(signaling, peerId, config, isCompatible);
                try
                {
                    await app.Register(provider);
                }
                catch (ProviderAlreadySetException e)
                {
                    Console.WriteLine(e);
                    throw;
                }
                
                Peers[peerId] = provider;
            }
        }
        else
        {
            throw new BadRegisterException(message["data"].ToString() ?? "");
        }
        Peers[peerId].OnPeerDead += OnPeerDead;
        
    }

    public async Task ListenExceptions(Func<Task> run)
    {
        try
        {
            await run();
        }
        catch (ProviderAlreadySetException e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
    private Task OnGeneralConnectionEvent(NonPeerConnection connection, ConnectionEventType eventType)
        => Task.Run(() =>
        {
            switch (eventType)
            {
                case ConnectionEventType.StateChanged:
                    _logger.LogCrossport(connection.State switch
                    {
                        ConnectionState.ConsumerRequested => CrossportEvents.NpcConsumerRequested,
                        ConnectionState.ProviderAnswered => CrossportEvents.NpcProviderAnswered,
                        ConnectionState.ProviderRequested => CrossportEvents.NpcProviderRequested,
                        ConnectionState.Established => CrossportEvents.NpcEstablished,
                        _ => throw new ArgumentOutOfRangeException()
                    }, "Npc {id} has a new status {cstatus} now.", connection.Id, Enum.GetName(connection.State));
                    break;
                case ConnectionEventType.Timeout:
                    _logger.LogCrossport(CrossportEvents.NpcTimeout,
                        "Npc {id} stuck at status {cstatus} for over {ttl} ms, and is to be destroyed.", connection.Id,
                        Enum.GetName(connection.State), NonPeerConnection.OfferedConnectionLifetime);
                    break;
                case ConnectionEventType.Destroyed:
                    _logger.LogCrossport(CrossportEvents.NpcDestroyed,
                        "Npc {id} is destroyed. Provider: {pstatus}. Consumer: {cstatus}",
                        connection.Id, Enum.GetName(connection.Provider?.Status ?? PeerStatus.Raw),
                        Enum.GetName(connection.Consumer.Status));
                    break;
                case ConnectionEventType.Created:
                    _logger.LogCrossport(CrossportEvents.NpcCreated, "Npc {id} is created.", connection.Id);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(eventType), eventType, null);
            }
        });
    private void OnPeerDead(Peer obj)
    {
        _logger.LogCrossport(CrossportEvents.PeerDead, "Peer {id} is dead after {ttl} ms waiting for reconnection.", obj.Id, Peer.LostPeerLifetime);
    }
}