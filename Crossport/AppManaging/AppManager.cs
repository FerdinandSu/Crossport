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
                await app.Register(consumer);
                Peers[peerId] = consumer;
            }
            else
            {
                var provider = new ContentProvider(signaling, peerId, config, isCompatible);
                await app.Register(provider);
                Peers[peerId] = provider;
            }
        }
        else
        {
            throw new BadRegisterException(message["data"].ToString() ?? "");
        }

    }
}