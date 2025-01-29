using Framework.Api.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Framework.Api.Demo.Controllers;

[ApiController]
public sealed class ProblemsController : ApiControllerBase
{
    [HttpGet("/mvc/malformed-syntax")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetMalformedSyntax()
    {
        return MalformedSyntax();
    }
}
