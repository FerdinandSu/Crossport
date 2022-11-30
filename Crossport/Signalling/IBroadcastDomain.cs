namespace Crossport.Signalling;

public interface IBroadcastDomain
{
    public Task<int> Broadcast<T>(T message, Predicate<WebRtcPeer> exceptBy);
}