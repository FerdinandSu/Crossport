using Crossport.Core.Entities;
using Crossport.Core.Signalling;

namespace Crossport.Core.Connecting;

public class ContentProvider : Peer
{
    public ContentProvider(ISignalingHandler signaling, Guid id, CrossportConfig config, bool isCompatible) : base(
        signaling, id, config, isCompatible)
    {
        Capacity = config.Capacity;
    }

    public int Capacity { get; }
}