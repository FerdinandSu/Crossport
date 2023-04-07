using System.Collections.Concurrent;

namespace Crossport.AppManaging;
public enum ConnectionEventType
{
    StateChanged = 1,
    Timeout = 2,
    Destroyed = 3,
    Created= 0,
}

public delegate Task GeneralConnectionEventHandler(NonPeerConnection connection, ConnectionEventType eventType);
/// <summary>
/// Manage lifetime of Connection and Cell.
/// </summary>
public class AppComponent
{
    private readonly AppInfo _info;
    private readonly GeneralConnectionEventHandler _connectionEventCallback;
    private readonly ConcurrentDictionary<ContentProvider, Cell> _cells = new();
    private readonly ConcurrentQueue<(ContentConsumer, NonPeerConnection)> _queuedConsumers = new();
    public AppComponent(AppInfo info, GeneralConnectionEventHandler connectionEventCallback)
    {
        _info = info;
        _connectionEventCallback = connectionEventCallback;
    }

    public void Register(ContentConsumer consumer)
    {
        consumer.OnConnect += Consumer_OnConnect;
        //consumer.OnPeerDead += Consumer_OnPeerDead;

    }

    private async Task Consumer_OnConnect(Peer sender, string connectionId)
    {
        if (sender is not ContentConsumer consumer)
        {
            throw new ArgumentException("Only ContentConsumer is allowed to create connection.", nameof(sender));

        }
        var availableCell = _cells.Values.FirstOrDefault(c => !c.IsFull);
        var connection = new NonPeerConnection(_info, consumer, connectionId);
        connection.OnDestroyed += Connection_OnDestroyed;
        connection.OnTimeout += Connection_OnTimeout;
        connection.OnStateChanged += Connection_OnStateChanged;
        if (availableCell != null)
        {
            await availableCell.Connect(consumer, connection);
        }
        else
        {
            _queuedConsumers.Enqueue((consumer, connection));
        }

        await _connectionEventCallback(connection, ConnectionEventType.Created);
    }
    private Task Connection_OnStateChanged(NonPeerConnection sender)
    {
        return _connectionEventCallback(sender, ConnectionEventType.StateChanged);
    }

    private async Task Connection_OnTimeout(NonPeerConnection sender)
    {
        await _connectionEventCallback(sender, ConnectionEventType.Timeout);
        await sender.Destroy();
    }

    private async Task Connection_OnDestroyed(NonPeerConnection sender)
    {
        sender.OnDestroyed -= Connection_OnDestroyed;
        sender.OnTimeout -= Connection_OnTimeout;
        sender.OnStateChanged -= Connection_OnStateChanged;
        if (sender.Consumer.Status == PeerStatus.Dead)
        {
            sender.Consumer.OnConnect -= Consumer_OnConnect;
        }
        await _connectionEventCallback(sender, ConnectionEventType.Destroyed);
    }

    public async Task<Cell> Register(ContentProvider provider)
    {
        var cell = new Cell(provider);
        cell.OnProviderDead += Cell_OnProviderDead;
        while (!cell.IsFull && _queuedConsumers.TryDequeue(out var tuple))
        {
            var (consumer,connection) = tuple;
            await cell.Connect(consumer, connection);
        }
        _cells[provider]=cell;
        return cell;
    }

    private void Cell_OnProviderDead(Cell sender)
    {
        _ = _cells.TryRemove(sender.Provider, out _);
    }
}