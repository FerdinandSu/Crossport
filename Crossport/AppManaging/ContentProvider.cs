namespace Crossport.AppManaging;

public class ContentProvider:Peer
{
    public int Capacity { get; }
    public ContentProvider(ISignalingHandler signaling, Guid id, CrossportConfig config, bool isCompatible) : base(signaling, id, config, isCompatible)
    {
        Capacity = config.Character;
    }
}