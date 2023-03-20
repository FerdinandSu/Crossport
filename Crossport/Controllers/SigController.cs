using Crossport.Signalling;
using Crossport.Signalling.Prototype;
using Crossport.WebSockets;
using Microsoft.AspNetCore.Mvc;

namespace Crossport.Controllers;

[Route("sig")]
public class SigController : Controller
{
    private ISignallingHandler SignallingHandler { get; }
    private CancellationToken HostShutdown { get; }
    public SigController(ISignallingHandler signallingHandler, IHostApplicationLifetime applicationLifetime)
    {
        SignallingHandler = signallingHandler;
        HostShutdown = applicationLifetime.ApplicationStopping;
    }
    [HttpGet("")]
    [HttpConnect("")]
    public async Task<IActionResult> Signalling()
    {
        return Redirect("ws://localhost/sig/ext");

        if (HttpContext.WebSockets.IsWebSocketRequest)
        {
            using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
            var tsc = new TaskCompletionSource();
            var session = new WebRtcPeer(webSocket, tsc, HostShutdown);

            SignallingHandler.Add(session);
            await session.ListenAsync();
            await tsc.Task;
            await SignallingHandler.Remove(session);
        }
        else HttpContext.Response.StatusCode = StatusCodes.Status400BadRequest;

    }
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
}