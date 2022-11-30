using Crossport.Signalling;
using Crossport.Signalling.Prototype;
using Crossport.WebSockets;
using Microsoft.AspNetCore.Mvc;

namespace Crossport.Controllers
{
    public class SigController : Controller
    {
        private ISignallingHandler SignallingHandler { get; }
        private CancellationToken HostShutdown { get; }
        public SigController(ISignallingHandler signallingHandler,IHostApplicationLifetime applicationLifetime)
        {
            SignallingHandler = signallingHandler;
            HostShutdown = applicationLifetime.ApplicationStopping;
        }
        [HttpGet("/sig")]
        [HttpConnect("/sig")]
        public async Task<IActionResult> Signalling()
        {
            return Redirect("ws://localhost/sig2");

            if (HttpContext.WebSockets.IsWebSocketRequest)
            {
                using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
                var tsc = new TaskCompletionSource();
                var session = new WebRtcPeer(webSocket,tsc, HostShutdown);
                
                SignallingHandler.Add(session);
                await session.ListenAsync();
                await tsc.Task;
                await SignallingHandler.Remove(session);
            }
            else HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;

        }
        [HttpGet("/sig2")]
        [HttpConnect("/sig2")]
        public async Task Signalling2()
        {
            if (HttpContext.WebSockets.IsWebSocketRequest)
            {
                using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
                var tsc = new TaskCompletionSource();
                var session = new CrossportPeer(webSocket, tsc, HostShutdown);

                SignallingHandler.Add(session);
                await session.ListenAsync();
                await tsc.Task;
                await SignallingHandler.Remove(session);
            }
            else HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;

        }
    }
}
