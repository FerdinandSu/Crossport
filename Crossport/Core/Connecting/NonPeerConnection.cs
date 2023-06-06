using System.Text.Json;
using Crossport.Core.Entities;
using Crossport.Utils;

namespace Crossport.Core.Connecting;

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
    public ProviderAlreadySetException(string message, NonPeerConnection npc) : base(message)
    {
        Npc = npc;
    }

    public NonPeerConnection Npc { get; }
}

[Serializable]
public class IllegalSignalingException : Exception
{
    public enum IllegalSignalingType
    {
        NullMessage = 0,
        ConsumerOfferToNonPending = 1,

        ConsumerAnswerToNonRequested = 2,
        ConsumerMessageToNullProvider = 3,

        //ConsumerRequestedConnection=4,
        //ProviderRequestedConnection =-4,
        ProviderOfferToNonAnswered = -1,
        ProviderAnswerToNonRequested = -2
    }
    //
    // For guidelines regarding the creation of new exception types, see
    //    http://msdn.microsoft.com/library/default.asp?url=/library/en-us/cpgenref/html/cpconerrorraisinghandlingguidelines.asp
    // and
    //    http://msdn.microsoft.com/library/default.asp?url=/library/en-us/dncscol/html/csharp07192001.asp
    //

    public IllegalSignalingException(NonPeerConnection connection, IllegalSignalingType type, JsonElement data)
        : base($"Illegal Signaling on connection {connection.Id}: {Enum.GetName(type)}")
    {
        Connection = connection;
        Type = type;
        SignallingData = data;
    }

    public NonPeerConnection Connection { get; }
    public IllegalSignalingType Type { get; }
    public JsonElement SignallingData { get; }
}

public delegate Task ConnectionEventHandler(NonPeerConnection sender);

/// <summary>
///     WebRTC Connection, NOT Websocket connection.
/// </summary>
public class NonPeerConnection
{
    public static int OfferedConnectionLifetime = 5000;
    private readonly string _consumerIndicatedId;

    private readonly SemaphoreSlim _setProviderLock = new(1, 1);

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

    public AppInfo App { get; }
    public ContentConsumer Consumer { get; }
    public ContentProvider? Provider { get; private set; }
    private bool HasProvider => Provider != null;
    private bool IsStable => State == ConnectionState.Established;
    public ConnectionState State { get; private set; }
    public string Id { get; private set; }

    private static long JsNow => DateTime.Now.ToJavascriptTimeStamp();
    public event ConnectionEventHandler? OnTimeout;
    public event ConnectionEventHandler? OnDestroyed;
    public event ConnectionEventHandler? OnStateChanged;

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
        if (HasProvider) throw new ProviderAlreadySetException($"Connection {Id} already have Provider!", this);

        await _setProviderLock.WaitAsync();
        if (Provider != null) return;
        Provider = provider;
        Id = $"{App.Application}:{App.Component}:{Provider.Id}@{Consumer.Id}";
        await Consumer.SendAsync(new { type = "connect", connectionId = _consumerIndicatedId, polite = true });
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
                IllegalSignalingException.IllegalSignalingType.ProviderAnswerToNonRequested, data);

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
        if (IsStable) return;
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
                IllegalSignalingException.IllegalSignalingType.ProviderOfferToNonAnswered, data);
        var offer = new Offer(Extract<OfferAnswerStruct>(data).Sdp, JsNow, false);
        await Consumer.SendAsync(new { from = _consumerIndicatedId, to = Consumer.Id, type = "offer", data = offer });
        await ChangeState(ConnectionState.ProviderRequested);
        _ = Task.Delay(OfferedConnectionLifetime).ContinueWith(async _ =>
        {
            if (State == ConnectionState.ProviderRequested) await (OnTimeout?.Invoke(this) ?? Task.CompletedTask);
        });
    }

    public async Task Destroy()
    {
        if (State is ConnectionState.Disconnected) return;
        _ = Provider?.SendAsync(new { type = "disconnect", connectionId = Id });
        _ = Consumer.SendAsync(new { type = "disconnect", connectionId = _consumerIndicatedId });
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
                IllegalSignalingException.IllegalSignalingType.ConsumerAnswerToNonRequested, data);
        if (Provider is null)
            throw new IllegalSignalingException(this,
                IllegalSignalingException.IllegalSignalingType.ConsumerMessageToNullProvider, data);

        await Provider.SendAsync(new
        {
            from = Id,
            to = Provider.Id,
            type = "answer",
            data = new Answer(Extract<OfferAnswerStruct>(data).Sdp, DateTime.Now.ToJavascriptTimeStamp())
        });
        await ChangeState(ConnectionState.Established);
    }

    private async Task Consumer_OnCandidate(Peer sender, string from, string to, JsonElement data)
    {
        if (IsStable) return;
        var message = Extract<CandidateStruct>(data);

        var candidate = new CandidateRecord(message.Candidate, message.SdpMLineIndex, message.SdpMid.ToString(),
            JsNow);
        await _setProviderLock.WaitAsync();
        if (!HasProvider)
            throw new IllegalSignalingException(this,
                IllegalSignalingException.IllegalSignalingType.ConsumerMessageToNullProvider, data);
        _setProviderLock.Release();
        await Provider!.SendAsync(new
        {
            from = Id,
            to = Provider.Id,
            type = "candidate",
            data = candidate
        });
    }

    private async Task SendConsumerOffer(Offer offer)
    {
        await Provider!.SendAsync(new { from = Id, to = Provider.Id, type = "offer", data = offer });
        await ChangeState(ConnectionState.ConsumerRequested);
        _ = Task.Delay(OfferedConnectionLifetime).ContinueWith(async _ =>
        {
            if (State == ConnectionState.ConsumerRequested) await (OnTimeout?.Invoke(this) ?? Task.CompletedTask);
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
        if (message is null)
            throw new IllegalSignalingException(this,
                IllegalSignalingException.IllegalSignalingType.NullMessage, data);
        return message;
    }

    private async Task Consumer_OnOffer(Peer sender, string from, string to, JsonElement data)
    {
        if (State is not ConnectionState.Pending or ConnectionState.ConsumerRequested)
            throw new IllegalSignalingException(this,
                IllegalSignalingException.IllegalSignalingType.ConsumerOfferToNonPending, data);
        var newOffer = new Offer(Extract<OfferAnswerStruct>(data).Sdp, JsNow, false);
        await _setProviderLock.WaitAsync();
        if (!HasProvider)
            throw new IllegalSignalingException(this,
                IllegalSignalingException.IllegalSignalingType.ConsumerMessageToNullProvider, data);
        _setProviderLock.Release();
        await SendConsumerOffer(newOffer);
    }

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
}