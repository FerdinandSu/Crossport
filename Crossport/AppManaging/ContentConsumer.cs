namespace Crossport.AppManaging;

public class ContentConsumer:Peer
{
    public ContentConsumer(ISignalingHandler signaling, Guid id, CrossportConfig config, bool isCompatible) : base(signaling, id, config, isCompatible)
    {
    }
}