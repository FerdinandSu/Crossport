using System.Text.Json;

namespace Crossport.AppManaging;

public class AppManager
{
    public async Task RegisterOrRenew(ISignalingHandler signaling, Dictionary<string, object> message, bool isCompatible)
    {
        var config = ((JsonElement)message["data"]).DeserializeWeb<CrossportConfig>();
        
        var peerId = Guid.Parse(message.SafeGetString("id"));
    }
}