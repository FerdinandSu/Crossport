﻿namespace Crossport.Core.Connecting;

public delegate void ProviderDeadEventHandler(Cell sender);

/// <summary>
///     A broadcast domain with 1 Provider and N Consumers.
///     Notice: Cell WILL NOT manages lifetime of connection and provider.
/// </summary>
public class Cell
{
    public Cell(ContentProvider provider)
    {
        Provider = provider;
    }

    public Dictionary<ContentConsumer, NonPeerConnection> Consumers { get; } = new();

    public ContentProvider Provider { get; }

    public bool IsFull =>
        Provider.Capacity <= Consumers.Count;

    public bool IsAvailable
    {
        get
        {
            if (IsFull) return false;
            if (Provider.Status == PeerStatus.Dead)
            {
                OnProviderDead?.Invoke(this);
                return false;
            }

            if (Provider.Status == PeerStatus.Lost) return false;
            return true;
        }
    }

    public event ProviderDeadEventHandler? OnProviderDead;

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
}