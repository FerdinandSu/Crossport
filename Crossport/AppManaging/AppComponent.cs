using System.Collections.Concurrent;

namespace Crossport.AppManaging;

public class AppComponent
{
    private readonly AppInfo _info;
    private ConcurrentBag<Cell> _cells=new();
    private ConcurrentQueue<ContentConsumer> _queuedConsumers=new();
    public AppComponent(AppInfo info)
    {
        _info = info;
    }

    public async Task Register(ContentConsumer consumer)
    {
        consumer.OnConnect += Consumer_OnConnect;

        var availableCell = _cells.FirstOrDefault(c => !c.IsFull);
        if (availableCell != null)
        {
            availableCell.
        }
    }

    private Task Consumer_OnConnect(Peer sender, string connectionId)
    {
        throw new NotImplementedException();
    }

    public async Task Register(ContentProvider provider)
    {

    }
}