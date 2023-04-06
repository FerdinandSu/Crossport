using System.Collections.Concurrent;

namespace Crossport.AppManaging;

public class Cell
{
    public Cell(ContentProvider provider)
    {
        Provider = provider;
    }

    public ConcurrentDictionary<ContentConsumer, NonPeerConnection> Consumers { get; } = new();
    public ContentProvider Provider { get; private set; }

    public bool IsFull =>
        Provider.Capacity > Consumers.Count;
}