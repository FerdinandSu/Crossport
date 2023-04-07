using Crossport.AppManaging;
using Crossport.Signalling;
using Crossport.Signalling.Prototype;
using Crossport.WebSockets;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Crossport.Controllers;

[Route("sig")]
public class SigController : Controller
{
    private readonly AppManager _appManager;
    private readonly IConfiguration _config;
    private int RawPeerLifetime => _config.GetSection("Crossport:ConnectionManagement").GetValue<int>("RawPeerLifetime");
    private readonly ILogger<SigController> _logger;
    private CancellationToken HostShutdown { get; }
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

    [HttpGet("{app}/{component}")]
    [HttpConnect("{app}/{component}")]
    public async Task CompatibleConnect([FromRoute] string app, [FromRoute] string component, [FromQuery] string id, [FromQuery] int capacity)
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
                Character = capacity
            };
            await _appManager.RegisterOrRenew(session, id, config, true);
            await _appManager.ListenExceptions(() => session.ListenAsync());


            await tsc.Task;
        }
        else
        {
            HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await HttpContext.Response.WriteAsync("Only WebSocket Connections are allowed.", cancellationToken: HostShutdown);
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
                    new(r => r.SafeGetString("type").ToLower() == "register",
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
            await HttpContext.Response.WriteAsync("Only WebSocket Connections are allowed.", cancellationToken: HostShutdown);
        }
    }
}