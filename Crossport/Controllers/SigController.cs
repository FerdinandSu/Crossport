using Crossport.WebSockets;
using Microsoft.AspNetCore.Mvc;

namespace Crossport.Controllers
{
    public class SigController : Controller
    {
        private SignallingHandler SignallingHandler { get; }
        private CancellationToken HostShutdown { get; }
        public SigController(SignallingHandler signallingHandler,IHostApplicationLifetime applicationLifetime)
        {
            SignallingHandler = signallingHandler;
            HostShutdown = applicationLifetime.ApplicationStopping;
        }
        [HttpGet("/sig")]
        [HttpConnect("/sig")]
        public async Task Get()
        {
            if (HttpContext.WebSockets.IsWebSocketRequest)
            {
                using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
                var tsc = new TaskCompletionSource();
                var session = new WebRtcSession(webSocket, HostShutdown,tsc);
                
                SignallingHandler.Add(session);
                await session.ListenAsync();
                await tsc.Task;
                await SignallingHandler.Remove(session);
            }
            else HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;

        }
    }
}
