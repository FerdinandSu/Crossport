using System.Collections.Concurrent;

namespace Crossport.AppManaging;

public class Cell
{
    public ConcurrentDictionary<ContentConsumer, NonPeerConnection?> Consumers { get; } = new();
    public ContentProvider? Provider { get; private set; }

    public bool? IsFull
    {
        get
        {
            if (Provider == null) return null;
            return Provider.Capacity > Consumers.Count;
        }
    }
}