using Crossport.AppManaging;
using Crossport.Signalling;
using Crossport.Signalling.Prototype;
using Crossport.WebSockets;
using Microsoft.AspNetCore.Mvc;

namespace Crossport.Controllers;

[Route("sig")]
public class SigController : Controller
{
    private readonly AppManager _appManager;
    private readonly IConfiguration _config;
    private int RawPeerLifetime => _config.GetSection("Crossport:ConnectionManagement").GetValue<int>("RawPeerLifetime");
    private readonly ILogger<SigController> _logger;
    private ISignallingHandler SignallingHandler { get; }
    private CancellationToken HostShutdown { get; }
    public SigController(ISignallingHandler signallingHandler, IHostApplicationLifetime applicationLifetime,
        AppManager appManager,
        IConfiguration configuration,
        ILogger<SigController> logger)
    {
        _appManager = appManager;
        _config = configuration;
        _logger = logger;
        SignallingHandler = signallingHandler;
        HostShutdown = applicationLifetime.ApplicationStopping;
    }
    //[HttpGet("")]
    //[HttpConnect("")]
    //public async Task<IActionResult> Signalling()
    //{
    //    var request = HttpContext.Request;
    //    var redirect = $"{request.Scheme.Replace("http","ws")}://{request.Host}/sig/ext";
    //    _logger.LogDebug("Redirect: {url}", redirect);
    //    return Redirect(redirect);

    //    if (HttpContext.WebSockets.IsWebSocketRequest)
    //    {
    //        using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
    //        var tsc = new TaskCompletionSource();
    //        var session = new WebRtcPeer(webSocket, tsc, HostShutdown);

    //        SignallingHandler.Add(session);
    //        await session.ListenAsync();
    //        await tsc.Task;
    //        await SignallingHandler.Remove(session);
    //    }
    //    else HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;

    //}
    [HttpGet("ext")]
    [HttpConnect("ext")]
    public async Task SignallingExtendedRegistering()
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
    [HttpGet("inline/{app}/{component}/{id}")]
    [HttpConnect("inline/{app}/{component}/{id}")]
    public async Task SignallingInlineRegistering([FromRoute] string app, [FromRoute] string component, [FromRoute] string id)
    {
        if (HttpContext.WebSockets.IsWebSocketRequest)
        {
            using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
            var tsc = new TaskCompletionSource();
            var session = new CrossportPeer(webSocket,
                new CrossportConfig { AllowAnonymous = false, Application = app, Component = component, Character = CrossportCharacter.MediaConsumer }, id, tsc, HostShutdown);

            SignallingHandler.Add(session);
            await session.ListenAsync();
            await tsc.Task;
            await SignallingHandler.Remove(session);
        }
        else HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;

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
                        (sender, msg) => _appManager.RegisterOrRenew(sender, msg, false)
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