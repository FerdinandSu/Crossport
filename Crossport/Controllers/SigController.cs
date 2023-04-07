using System.Text.Json;
using Crossport.Core;
using Crossport.Core.Entities;
using Crossport.Core.Signalling;
using Crossport.Utils;
using Microsoft.AspNetCore.Mvc;

namespace Crossport.Controllers;

[Route("sig")]
public class SigController : Controller
{
    private readonly AppManager _appManager;
    private readonly IConfiguration _config;
    private readonly ILogger<SigController> _logger;

    public SigController(IHostApplicationLifetime applicationLifetime,
        AppManager appManager,
        IConfiguration configuration,
        ILogger<SigController> logger)
    {
        _appManager = appManager;
        _config = configuration;
        _logger = logger;
        HostShutdown = applicationLifetime.ApplicationStopping;
    }

    private int RawPeerLifetime =>
        _config.GetSection("Crossport:ConnectionManagement").GetValue<int>("RawPeerLifetime");

    private CancellationToken HostShutdown { get; }

    [HttpGet("{app}/{component}")]
    [HttpConnect("{app}/{component}")]
    public async Task CompatibleConnect([FromRoute] string app, [FromRoute] string component, [FromQuery] string id,
        [FromQuery] int capacity)
    {
        if (HttpContext.WebSockets.IsWebSocketRequest)
        {
            var tsc = new TaskCompletionSource();
            using var rawPeerTtlSource = new CancellationTokenSource(RawPeerLifetime);
            using var session = new WebSocketSignalingHandler(
                await HttpContext.WebSockets.AcceptWebSocketAsync(), tsc, HostShutdown);
            var config = new CrossportConfig
            {
                Application = app,
                Component = component,
                Capacity = capacity
            };
            await _appManager.RegisterOrRenew(session, id, config, true);
            await _appManager.ListenExceptions(() => session.ListenAsync());


            await tsc.Task;
        }
        else
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await HttpContext.Response.WriteAsync("Only WebSocket Connections are allowed.", HostShutdown);
        }
    }

    [HttpGet("")]
    [HttpConnect("")]
    public async Task StandardConnect()
    {
        if (HttpContext.WebSockets.IsWebSocketRequest)
        {
            var tsc = new TaskCompletionSource();
            using var rawPeerTtlSource = new CancellationTokenSource(RawPeerLifetime);
            using var session = new WaitForWebSocketSignalingHandler(
                    await HttpContext.WebSockets.AcceptWebSocketAsync(),
                    new WaitForWebSocketSignalingHandler.WaitFor(r => r.SafeGetString("type").ToLower() == "register",
                        (sender, message) => _appManager.RegisterOrRenew(sender, message.SafeGetString("id"),
                            ((JsonElement)message["data"]).DeserializeWeb<CrossportConfig>(), false)
                        , rawPeerTtlSource.Token),
                    tsc, HostShutdown
                )
                ;
            await _appManager.ListenExceptions(() => session.ListenAsync());


            await tsc.Task;
        }
        else
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await HttpContext.Response.WriteAsync("Only WebSocket Connections are allowed.", HostShutdown);
        }
    }
}