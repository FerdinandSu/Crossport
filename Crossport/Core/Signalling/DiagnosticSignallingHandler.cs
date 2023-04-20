using Crossport.Utils;

namespace Crossport.Core.Signalling;

public class DiagnosticSignallingHandler : ISignalingHandler
{
    private readonly ILogger<DiagnosticSignallingHandler> _logger;
    private readonly ISignalingHandler _baseHandler;
    public event SignalingDisconnectHandler? OnDisconnect;
    public event SignalingMessageHandler? OnMessage;

    public DiagnosticSignallingHandler(ILogger<DiagnosticSignallingHandler> logger, ISignalingHandler baseHandler)
    {
        _logger = logger;
        _baseHandler = baseHandler;
        _baseHandler.OnMessage += Base_OnMessage;
        _baseHandler.OnDisconnect += Base_OnDisconnect;
    }

    private async Task Base_OnDisconnect(ISignalingHandler sender)
    {
        await (OnDisconnect?.Invoke(this) ?? Task.CompletedTask);
    }

    private async Task Base_OnMessage(ISignalingHandler sender, Dictionary<string, object> message)
    {
        if (message.SafeGetString("type").ToLower() == "debug")
        {
            _logger.LogCrossport(CrossportEvents.CrossportDiagnosticDebugMessage, "Debug Signalling Message: {message}",
                message);
        }
        else
        {
            _logger.LogCrossport(CrossportEvents.CrossportDiagnosticSignallingMessage, "PassBy Signalling Message: {message}",
                message);
            await (OnMessage?.Invoke(this, message) ?? Task.CompletedTask);
        }
    }

    public async Task<bool> SendAsync<T>(T message)
    {
        return await _baseHandler.SendAsync(message);
    }

    public async Task DisconnectAsync()
    {
        await _baseHandler.DisconnectAsync();
    }
}

public class DiagnosticSignallingHandlerFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly Dictionary<ISignalingHandler, DiagnosticSignallingHandler> _wrappeds=new();
    public IReadOnlyDictionary<ISignalingHandler,DiagnosticSignallingHandler> Wrappeds=> _wrappeds;

    public DiagnosticSignallingHandlerFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public DiagnosticSignallingHandler Wrap(ISignalingHandler signalingHandler)
    {
        var r= new DiagnosticSignallingHandler(_loggerFactory.CreateLogger<DiagnosticSignallingHandler>(), signalingHandler);
        _wrappeds.Add(signalingHandler, r);
        signalingHandler.OnDisconnect += R_OnDisconnect;
        return r;
    }

    private async Task R_OnDisconnect(ISignalingHandler sender)
    {
        _wrappeds.Remove(sender);
        await Task.CompletedTask;
    }
}