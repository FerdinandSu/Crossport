using System.Collections.Concurrent;

namespace Crossport.AppManaging;



public delegate void ProviderDeadEventHandler(Cell sender);
/// <summary>
/// A broadcast domain with 1 Provider and N Consumers.
/// Notice: Cell WILL NOT manages lifetime of connection and provider.
/// </summary>
public class Cell
{

    public event ProviderDeadEventHandler? OnProviderDead;
    public Cell( ContentProvider provider)
    {
        Provider = provider;
    }

    public Dictionary<ContentConsumer, NonPeerConnection> Consumers { get; } = new();
    
    public ContentProvider Provider { get; }

    public async Task Connect(ContentConsumer consumer, NonPeerConnection connection)
    {
        
        connection.OnDestroyed += Connection_OnDestroyed;
        Consumers[consumer] = connection;
        await connection.SetProvider(Provider);
    }



    private async Task Connection_OnDestroyed(NonPeerConnection sender)
    {
        sender.OnDestroyed -= Connection_OnDestroyed;
        Consumers.Remove(sender.Consumer);
        if (Provider.Status == PeerStatus.Dead)
            OnProviderDead?.Invoke(this);
        await Task.CompletedTask;
    }

    public bool IsFull =>
        Provider.Capacity <= Consumers.Count;
}