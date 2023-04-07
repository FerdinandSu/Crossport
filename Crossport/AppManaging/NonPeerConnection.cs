using System.Runtime.Serialization;
using Crossport.RtcEntities;
using Crossport.Signalling.Prototype;
using Crossport.Signalling;
using System.Text.Json;
using System;

namespace Crossport.AppManaging;

public enum ConnectionState
{
    Disconnected = -1,
    Pending = 0,
    ConsumerRequested = 1,
    ProviderAnswered = 2,
    ProviderRequested = 3,
    Established = 4
}

[Serializable]
public class ProviderAlreadySetException : Exception
{

    public ProviderAlreadySetException(string message) : base(message)
    {
    }

}
[Serializable]
public class IllegalSignalingException : Exception
{
    public NonPeerConnection Connection { get; }
    public IllegalSignalingType Type { get; }

    public enum IllegalSignalingType
    {
        NullMessage = 0,
        ConsumerOfferToNonPending = 1,

        ConsumerAnsweredToNonRequested = 2,
        ConsumerAnsweredToNullProvider = 3,
        //ConsumerRequestedConnection=4,
        //ProviderRequestedConnection =-4,
        ProviderOfferToNonAnswered = -1,
        ProviderAnsweredToNonRequested = -2,

    }
    //
    // For guidelines regarding the creation of new exception types, see
    //    http://msdn.microsoft.com/library/default.asp?url=/library/en-us/cpgenref/html/cpconerrorraisinghandlingguidelines.asp
    // and
    //    http://msdn.microsoft.com/library/default.asp?url=/library/en-us/dncscol/html/csharp07192001.asp
    //

    public IllegalSignalingException(NonPeerConnection connection, IllegalSignalingType type)
        : base($"Illegal Signaling on connection {connection.Id}: {Enum.GetName(type)}")
    {
        Connection = connection;
        Type = type;
    }

}

public delegate Task ConnectionEventHandler(NonPeerConnection sender);
/// <summary>
/// WebRTC Connection, NOT Websocket connection.
/// </summary>
public class NonPeerConnection
{
    public static int OfferedConnectionLifetime = 5000;
    public class OfferAnswerStruct
    {
        public string ConnectionId { get; set; } = "";
        public string Sdp { get; set; } = "";
    }

    public class CandidateStruct
    {
        public string ConnectionId { get; set; } = "";
        //public string Sdp { get; set; } = "";
        public string Candidate { get; set; } = "";
        public int SdpMLineIndex { get; set; }
        public int SdpMid { get; set; }
    }

    private readonly SemaphoreSlim _setProviderLock = new(1, 1);

    public AppInfo App { get; }
    public ContentConsumer Consumer { get; }
    public ContentProvider? Provider { get; private set; }
    private bool HasProvider => Provider != null;
    private bool IsStable => State == ConnectionState.Established;
    public ConnectionState State { get; private set; }
    private readonly string _consumerIndicatedId;
    public string Id { get; private set; }
    private Offer? _cachedOffer;
    private readonly Queue<CandidateRecord> _cachedCandidateRecords = new();
    public event ConnectionEventHandler? OnTimeout;
    public event ConnectionEventHandler? OnDestroyed;
    public event ConnectionEventHandler? OnStateChanged;
    public NonPeerConnection(AppInfo app, ContentConsumer consumer, string consumerIndicatedId)
    {
        App = app;
        Consumer = consumer;
        _consumerIndicatedId = consumerIndicatedId;
        consumer.OnOffer += Consumer_OnOffer;
        consumer.OnCandidate += Consumer_OnCandidate;
        consumer.OnAnswer += Consumer_OnAnswer;
        consumer.OnDisconnect += Consumer_OnDisconnect;
        consumer.OnPeerDead += OnPeerDead;
        Id = $"{App.Application}:{App.Component}:NoProvider@{consumer.Id}";
    }

    private async Task Consumer_OnDisconnect(Peer sender, string connectionId)
    {
        await Destroy();
    }

    private void OnPeerDead(Peer obj)
    {
        _ = Destroy();
    }

    public async Task SetProvider(ContentProvider provider)
    {
        if (HasProvider) { throw new ProviderAlreadySetException($"Connection {Id} already have Provider!"); }

        await _setProviderLock.WaitAsync();
        if(Provider != null) { return; }
        Provider = provider;
        Id = $"{App.Application}:{App.Component}:{Provider.Id}@{Consumer.Id}";
        if (_cachedOffer != null) { await SendConsumerOffer(_cachedOffer); }

        foreach (var candidateRecord in _cachedCandidateRecords)
        {
            await Provider!.SendAsync(new
            {
                from = Id,
                to = Provider.Id,
                type = "candidate",
                data = candidateRecord
            });
        }
        _setProviderLock.Release();
        Provider.OnOffer += Provider_OnOffer;
        Provider.OnCandidate += Provider_OnCandidate;
        Provider.OnAnswer += Provider_OnAnswer;
        Provider.OnDisconnect += Provider_OnDisconnect;
        Provider.OnPeerDead += OnPeerDead;
    }

    private async Task Provider_OnDisconnect(Peer sender, string connectionId)
    {
        if (connectionId == Id) await Destroy();
    }

    private async Task Provider_OnAnswer(Peer sender, string from, string to, JsonElement data)
    {
        if (from != Id) return;
        if (State != ConnectionState.ConsumerRequested)
            throw new IllegalSignalingException(this,
                IllegalSignalingException.IllegalSignalingType.ProviderAnsweredToNonRequested);

        await Consumer.SendAsync(new
        {
            from = _consumerIndicatedId,
            to = Consumer.Id,
            type = "answer",
            data = new Answer(Extract<OfferAnswerStruct>(data).Sdp, DateTime.Now.ToJavascriptTimeStamp())
        });
        await ChangeState(ConnectionState.ProviderAnswered);
    }

    private async Task Provider_OnCandidate(Peer sender, string from, string to, JsonElement data)
    {
        if (from != Id) return;
        if (IsStable)
        {
            return;
        }
        var message = Extract<CandidateStruct>(data);
        var candidate = new CandidateRecord(message.Candidate, message.SdpMLineIndex, message.SdpMid.ToString(),
            JsNow);
        await Consumer.SendAsync(new
        {
            from = _consumerIndicatedId,
            to = Consumer.Id,
            type = "candidate",
            data = candidate
        });

    }

    private async Task Provider_OnOffer(Peer sender, string from, string to, JsonElement data)
    {
        if (from != Id) return;
        if (State != ConnectionState.ProviderAnswered)
            throw new IllegalSignalingException(this,
                IllegalSignalingException.IllegalSignalingType.ProviderOfferToNonAnswered);
        var offer = new Offer(Extract<OfferAnswerStruct>(data).Sdp, JsNow, false);
        await Consumer.SendAsync(new { from = _consumerIndicatedId, to = Consumer.Id, type = "offer", data = offer });
        await ChangeState(ConnectionState.ProviderRequested);
        _ = Task.Delay(OfferedConnectionLifetime).ContinueWith(async _ =>
        {
            if (State == ConnectionState.ProviderRequested)
            {
                await (OnTimeout?.Invoke(this) ?? Task.CompletedTask);
            }
        });
    }

    public async Task Destroy()
    {
        if (State is ConnectionState.Disconnected) { return; }
        await (Provider?.SendAsync(new { type = "disconnect", connectionId = Id }) ?? Task.CompletedTask);
        await Consumer.SendAsync(new { type = "disconnect", connectionId = _consumerIndicatedId });
        await (OnDestroyed?.Invoke(this) ?? Task.CompletedTask);
        State = ConnectionState.Disconnected;
        _setProviderLock.Dispose();
        Consumer.OnOffer -= Consumer_OnOffer;
        Consumer.OnCandidate -= Consumer_OnCandidate;
        Consumer.OnAnswer -= Consumer_OnAnswer;
        Consumer.OnDisconnect -= Consumer_OnDisconnect;
        Consumer.OnPeerDead -= OnPeerDead;
        if (Provider == null) return;
        Provider.OnOffer -= Provider_OnOffer;
        Provider.OnCandidate -= Provider_OnCandidate;
        Provider.OnAnswer -= Provider_OnAnswer;
        Provider.OnDisconnect -= Provider_OnDisconnect;
        Provider.OnPeerDead -= OnPeerDead;
        
    }
    private async Task Consumer_OnAnswer(Peer sender, string from, string to, JsonElement data)
    {
        if (State is not ConnectionState.ProviderRequested)
            throw new IllegalSignalingException(this,
                IllegalSignalingException.IllegalSignalingType.ConsumerAnsweredToNonRequested);
        if (Provider is null)
            throw new IllegalSignalingException(this,
                IllegalSignalingException.IllegalSignalingType.ConsumerAnsweredToNullProvider);

        await Provider.SendAsync(new
        {
            from = Id,
            to = Provider.Id,
            type = "answer",
            data = new Answer(Extract<OfferAnswerStruct>(data).Sdp, DateTime.Now.ToJavascriptTimeStamp())
        });
        await ChangeState(ConnectionState.Established);
    }

    private static long JsNow => DateTime.Now.ToJavascriptTimeStamp();
    private async Task Consumer_OnCandidate(Peer sender, string from, string to, JsonElement data)
    {
        if (IsStable)
        {
            return;
        }
        var message = Extract<CandidateStruct>(data);

        var candidate = new CandidateRecord(message.Candidate, message.SdpMLineIndex, message.SdpMid.ToString(),
            JsNow);
        await _setProviderLock.WaitAsync();
        if (HasProvider)
        {
            await Provider!.SendAsync(new
            {
                from = Id,
                to = Provider.Id,
                type = "candidate",
                data = candidate
            });
        }
        else
        {
            _cachedCandidateRecords.Enqueue(candidate);
        }
        _setProviderLock.Release();
    }

    private async Task SendConsumerOffer(Offer offer)
    {
        await Provider!.SendAsync(new { from = Id, to = Provider.Id, type = "offer", data = offer });
        await ChangeState(ConnectionState.ConsumerRequested);
        _ = Task.Delay(OfferedConnectionLifetime).ContinueWith(async _ =>
        {
            if (State == ConnectionState.ConsumerRequested)
            {
                await (OnTimeout?.Invoke(this) ?? Task.CompletedTask);
            }
        });
    }

    private async Task ChangeState(ConnectionState state)
    {
        State = state;
        await (OnStateChanged?.Invoke(this) ?? Task.CompletedTask);
    }
    private T Extract<T>(JsonElement data)
    {
        var message = data.DeserializeWeb<T>();
        if (message is null) throw new IllegalSignalingException(this,
            IllegalSignalingException.IllegalSignalingType.NullMessage);
        return message;
    }
    private async Task Consumer_OnOffer(Peer sender, string from, string to, JsonElement data)
    {
        if (State is not ConnectionState.Pending or ConnectionState.ConsumerRequested)
        {
            throw new IllegalSignalingException(this,
                IllegalSignalingException.IllegalSignalingType.ConsumerOfferToNonPending);
        }
        var newOffer = new Offer(Extract<OfferAnswerStruct>(data).Sdp, JsNow, false);
        await _setProviderLock.WaitAsync();
        if (HasProvider)
        {
            await SendConsumerOffer(newOffer);
        }
        else
        {
            _cachedOffer = newOffer;
        }
        _setProviderLock.Release();

    }
}