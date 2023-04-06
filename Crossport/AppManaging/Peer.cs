namespace Crossport.AppManaging;

public enum PeerRole
{
    ContentConsumer = 0, ContentProvider = 1
}

public enum PeerStatus
{
    // Raw实际上不存在于任何Peer里，但是习惯上还是把它放在了这里
    Raw = 0,
    Standard = 1,
    Compatible = 2,
    Dead = 3
}

public abstract class Peer
{
    public static bool operator ==(Peer? left, Peer? right) => Equals(left, right);
    public static bool operator !=(Peer? left, Peer? right) => !Equals(left, right);
    protected bool Equals(Peer other) => Id.Equals(other.Id);

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        return obj.GetType() == GetType() && Equals((Peer)obj);
    }

    public override int GetHashCode()
    {
        return Id.GetHashCode();
    }

    protected Peer(ISignalingHandler signaling, Guid id, CrossportConfig config, bool isCompatible)
    {
        Id = id;
        _signaling = signaling;
        Status = isCompatible ? PeerStatus.Compatible : PeerStatus.Standard;
        Role = config.Character == 0 ? PeerRole.ContentConsumer : PeerRole.ContentProvider;

    }
    protected ISignalingHandler _signaling;
    public Guid Id { get; }
    public PeerStatus Status { get; private set; }
    public PeerRole Role { get; }

}