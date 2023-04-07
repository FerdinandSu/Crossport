using Crossport.Core.Entities;
using Crossport.Core.Signalling;

namespace Crossport.Core.Connecting;

public class ContentConsumer : Peer
{
    public ContentConsumer(ISignalingHandler signaling, Guid id, CrossportConfig config, bool isCompatible) : base(
        signaling, id, config, isCompatible)
    {
    }
}