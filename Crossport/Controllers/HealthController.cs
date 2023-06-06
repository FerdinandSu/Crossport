using Crossport.Core;
using Crossport.Core.Entities;
using Crossport.Core.Signalling;
using Crossport.Utils;
using Ices.MetaCdn.Remoting;
using Microsoft.AspNetCore.Mvc;

namespace Crossport.Controllers;

[Route("/health")]
public class HealthController : ControllerBase
{
    private readonly AppManager _appManager;

    public HealthController(IHostApplicationLifetime applicationLifetime, AppManager appManager)
    {
        _appManager = appManager;
        HostShutdown = applicationLifetime.ApplicationStopping;
    }

    public CancellationToken HostShutdown { get; set; }

    [HttpGet("listen")]
    [HttpConnect("listen")]
    public async Task Listen()
    {
        if (HttpContext.WebSockets.IsWebSocketRequest)
        {
            var tsc = new TaskCompletionSource();
            using var session = IRemoting.CreateFromWebSocket(
                await HttpContext.WebSockets.AcceptWebSocketAsync(), tsc, HostShutdown);
            _appManager.AddHealthListener(session);
            await _appManager.ListenExceptions(session.ListenAsync);
            await tsc.Task;
        }
        else
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await HttpContext.Response.WriteAsync("Only WebSocket Connections are allowed.", HostShutdown);
        }
    }
}