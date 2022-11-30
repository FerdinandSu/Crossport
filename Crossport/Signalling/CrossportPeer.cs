using System.Net.WebSockets;
using System.Text.Json;

namespace Crossport.Signalling;

public class CrossportPeer : WebRtcPeer
{
    public delegate Task RegisterEvent(CrossportPeer sender);

    public string Mode => (Config?.AllowAnonymous ?? false)
            ? "public"
            : "private";
    public event RegisterEvent? Register;
    public CrossportConfig? Config{ get; private set; }
    public string? ClientId{ get; private set; }
    public CrossportPeer(WebSocket socket, TaskCompletionSource completionSource, CancellationToken cancellationToken) :
        base(socket, completionSource, cancellationToken)
    {
    }

    protected override async Task ReceiveResponse(Dictionary<string, object> message)
    {
        var type = message.SafeGetString("type").ToLower();
        if (type == "register")
        {
            Config = ((JsonElement)message["data"]).DeserializeWeb<CrossportConfig>();
            ClientId = message.SafeGetString("id");
            await (Register?.Invoke(this) ?? Task.CompletedTask);
        }
        else await base.ReceiveResponse(message);
    }
}