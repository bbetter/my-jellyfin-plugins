using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace JellyShare;

[ApiController]
[Route("JellyShare")]
public class ClientScriptController : ControllerBase
{
    /// <summary>Serves the embedded client-side UI script.</summary>
    [HttpGet("ClientScript")]
    [AllowAnonymous]
    [ResponseCache(Duration = 3600)]
    public IActionResult GetClientScript()
    {
        var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("JellyShare.Resources.jellyshare.js");

        if (stream is null)
            return NotFound();

        return File(stream, "application/javascript; charset=utf-8");
    }
}
