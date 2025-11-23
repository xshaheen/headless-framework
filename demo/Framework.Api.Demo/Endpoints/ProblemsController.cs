using FluentValidation;
using FluentValidation.Results;
using Framework.Api.Mvc.Controllers;
using Framework.Exceptions;
using Framework.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Framework.Api.Demo.Endpoints;

[ApiController]
[Route("/mvc")]
#pragma warning disable CA1024 // Use properties where appropriate
public sealed class ProblemsController : ApiControllerBase
{
    [Authorize]
    [HttpGet("authorized")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetAuthorized()
    {
        return Ok();
    }

    [HttpGet("malformed-syntax")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetMalformedSyntax()
    {
        return MalformedSyntax();
    }

    [HttpPost("entity-not-found")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetEntityNotFound()
    {
        throw new EntityNotFoundException("Entity", "Key");
    }

    [HttpPost("internal-error")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetInternalError()
    {
        throw new InvalidOperationException("This is a test exception.");
    }

    [HttpPost("conflict")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetConflict()
    {
        throw new ConflictException(new ErrorDescriptor("error-code", @"Error message"));
    }

    [HttpPost("unprocessable-entity")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult GetUnprocessableEntity()
    {
        throw new ValidationException([new("Property", "Error message") { ErrorCode = "error-code" }]);
    }
}
