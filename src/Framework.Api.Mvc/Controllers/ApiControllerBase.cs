// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation.Results;
using Framework.Api.Abstractions;
using Framework.Primitives;
using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Api.Mvc.Controllers;

[PublicAPI]
[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    private IConfiguration? _config;
    private ISender? _sender;
    private IFrameworkProblemDetailsFactory? _problemDetailsCreator;

    protected IConfiguration Configuration =>
        _config ??=
            HttpContext.RequestServices.GetService<IConfiguration>()
            ?? throw new InvalidOperationException($"{nameof(IConfiguration)} service not registered");

    protected ISender Sender =>
        _sender ??=
            HttpContext.RequestServices.GetService<ISender>()
            ?? throw new InvalidOperationException($"{nameof(ISender)} service not registered");

    private IFrameworkProblemDetailsFactory Factory =>
        _problemDetailsCreator ??=
            HttpContext.RequestServices.GetService<IFrameworkProblemDetailsFactory>()
            ?? throw new InvalidOperationException($"{nameof(Factory)} service not registered");

    [NonAction]
    protected async Task<ActionResult> NoContent(IRequest? req, CancellationToken token = default)
    {
        if (req is null)
        {
            return MalformedSyntax();
        }

        await Sender.Send(req, token);

        return NoContent();
    }

    [NonAction]
    protected async Task<ActionResult<T>> Ok<T>(IRequest<T>? req, CancellationToken token = default)
    {
        return req is null ? MalformedSyntax() : Ok(await Sender.Send(req, token));
    }

    [NonAction]
    protected async Task<ActionResult> Ok(IRequest? req, CancellationToken token = default)
    {
        if (req is null)
        {
            return MalformedSyntax();
        }

        await Sender.Send(req, token);

        return Ok();
    }

    [NonAction]
    protected OkResult Ok(Unit _)
    {
        return Ok();
    }

    [NonAction]
    protected BadRequestObjectResult MalformedSyntax()
    {
        return base.BadRequest(Factory.MalformedSyntax(HttpContext));
    }

    [NonAction]
    protected UnprocessableEntityObjectResult UnprocessableEntityProblemDetails(IEnumerable<ValidationFailure> failures)
    {
        var errors = failures
            .GroupBy(
                static failure => failure.PropertyName,
                static failure => new ErrorDescriptor(failure.ErrorCode, failure.ErrorMessage),
                StringComparer.Ordinal
            )
            .ToDictionary(
                static failureGroup => failureGroup.Key,
                static failureGroup => (IReadOnlyList<ErrorDescriptor>)[.. failureGroup],
                StringComparer.Ordinal
            );

        var problemDetails = Factory.UnprocessableEntity(HttpContext, errors);

        return base.UnprocessableEntity(problemDetails);
    }

    [NonAction]
    protected NotFoundObjectResult NotFoundProblemDetails(string entity, string key)
    {
        var problemDetails = Factory.EntityNotFound(HttpContext, entity, key);

        return base.NotFound(problemDetails);
    }

    [NonAction]
    protected ConflictObjectResult ConflictProblemDetails(IEnumerable<ErrorDescriptor> errorDescriptors)
    {
        var problemDetails = Factory.Conflict(HttpContext, errorDescriptors);

        return base.Conflict(problemDetails);
    }

    [NonAction]
    protected ConflictObjectResult ConflictProblemDetails(ErrorDescriptor errorDescriptor)
    {
        var problemDetails = Factory.Conflict(HttpContext, [errorDescriptor]);

        return base.Conflict(problemDetails);
    }
}
