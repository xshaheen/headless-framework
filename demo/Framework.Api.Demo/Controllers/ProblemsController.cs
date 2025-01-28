using Microsoft.AspNetCore.Mvc;

namespace Framework.Api.Demo.Controllers;

[ApiController]
public sealed class ProblemsController : ControllerBase
{
    [HttpGet("/")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult Get()
    {
        return Ok("Hello World!");
    }
}
