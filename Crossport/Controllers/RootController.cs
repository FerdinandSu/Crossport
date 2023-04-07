using Microsoft.AspNetCore.Mvc;

namespace Crossport.Controllers;

[ApiController]
[Route("/")]
public class RootController : ControllerBase
{
    private readonly ILogger<RootController> _logger;

    public RootController(ILogger<RootController> logger)
    {
        _logger = logger;
    }

    [HttpGet("/config")]
    public IActionResult GetConfig()
    {
        return Ok(new { useWebSocket = true, startupMode = "public", logging = "dev" });
    }
}