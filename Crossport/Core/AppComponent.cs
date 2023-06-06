using System.Collections.Concurrent;
using Crossport.Core.Connecting;
using Ices.MetaCdn.Health;

namespace Crossport.Core;

public enum ConnectionEventType
{
    StateChanged = 1,
    Timeout = 2,
    Destroyed = 3,
    Created = 0
}

public delegate Task GeneralConnectionEventHandler(NonPeerConnection connection, ConnectionEventType eventType);

/// <summary>
///     Manage lifetime of Connection and Cell.
/// </summary>
public class AppComponent
{
    private readonly ConcurrentDictionary<ContentProvider, Cell> _cells = new();
    private readonly GeneralConnectionEventHandler _connectionEventCallback;
    public AppInfo Info{ get; }
    private readonly ConcurrentQueue<(ContentConsumer, NonPeerConnection)> _queuedConsumers = new();
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private HealthStats _lastHealthStats=new();
    private HealthStats ReportHealth()
    {
        var used= _cells.Sum(c=>c.Value.Consumers.Count);
        var total = _cells.Sum(c => c.Key.Capacity);
        var gray = _cells.Select(c => c.Key).Where(p => p.Status != PeerStatus.Lost).Sum(p => p.Capacity);
        var pending=_queuedConsumers.Count;
        total -= gray;
        var free = total - used;
        return new(free, total, pending, gray);
    }
    public delegate void AppComponentHealthChanged(AppComponent sender, HealthChange e);
    public event AppComponentHealthChanged? OnHealthChanged;

    private async Task UpdateHealth()
    {
        var newHealth=ReportHealth();
        await _semaphore.WaitAsync();
        var diff=newHealth- _lastHealthStats;
        switch (diff.Evaluation)
        {
            case > 0:
                OnHealthChanged?.Invoke(this, new(newHealth, diff, HealthEventType.LoadDecreased));
                break;
            case < 0:
                OnHealthChanged?.Invoke(this, new(newHealth, diff, HealthEventType.LoadIncreased));
                break;
            case double.NaN:
                OnHealthChanged?.Invoke(this, new(newHealth, diff, HealthEventType.NoProvider));
                break;
        }
        _lastHealthStats=newHealth;
        _semaphore.Release();
    }
    public AppComponent(AppInfo info, GeneralConnectionEventHandler connectionEventCallback)
    {
        Info = info;
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
            throw new ArgumentException("Only ContentConsumer is allowed to create connection.", nameof(sender));
        var availableCell = _cells.Values.FirstOrDefault(c => c.IsAvailable);
        var connection = new NonPeerConnection(Info, consumer, connectionId);
        connection.OnDestroyed += Connection_OnDestroyed;
        connection.OnTimeout += Connection_OnTimeout;
        connection.OnStateChanged += Connection_OnStateChanged;
        if (availableCell != null)
            await availableCell.Connect(consumer, connection);
        else
            _queuedConsumers.Enqueue((consumer, connection));
        await UpdateHealth();
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
        if (sender.Consumer.Status == PeerStatus.Dead) sender.Consumer.OnConnect -= Consumer_OnConnect;
        await UpdateHealth();
        await _connectionEventCallback(sender, ConnectionEventType.Destroyed);
    }

    public async Task<Cell> Register(ContentProvider provider)
    {
        var cell = new Cell(provider);
        cell.OnProviderDead += Cell_OnProviderDead;
        while (cell.IsAvailable && _queuedConsumers.TryDequeue(out var tuple))
        {
            var (consumer, connection) = tuple;
            if (consumer.Status is not PeerStatus.Standard or PeerStatus.Compatible ||
                connection.State == ConnectionState.Disconnected) continue;
            await cell.Connect(consumer, connection);
        }

        _cells[provider] = cell;
        await UpdateHealth();
        return cell;
    }

    private void Cell_OnProviderDead(Cell sender)
    {
        _ = _cells.TryRemove(sender.Provider, out _);

    }
}