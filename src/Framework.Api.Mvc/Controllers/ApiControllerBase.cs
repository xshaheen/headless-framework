// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using FluentValidation;
using FluentValidation.Results;
using Framework.Abstractions;
using Framework.Api.Abstractions;
using Framework.Primitives;
using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Api.Controllers;

[PublicAPI]
[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    [field: AllowNull, MaybeNull]
    protected IConfiguration Configuration =>
        field ??=
            HttpContext.RequestServices.GetService<IConfiguration>()
            ?? throw new InvalidOperationException($"{nameof(IConfiguration)} service not registered");

    [field: AllowNull, MaybeNull]
    protected ISender Sender =>
        field ??=
            HttpContext.RequestServices.GetService<ISender>()
            ?? throw new InvalidOperationException($"{nameof(ISender)} service not registered");

    [field: AllowNull, MaybeNull]
    protected MvcProblemDetailsNormalizer ProblemDetailsNormalizer =>
        field ??=
            HttpContext.RequestServices.GetService<MvcProblemDetailsNormalizer>()
            ?? throw new InvalidOperationException($"{nameof(MvcProblemDetailsNormalizer)} service not registered");

    [field: AllowNull, MaybeNull]
    private IProblemDetailsCreator ProblemDetailsCreator =>
        field ??=
            HttpContext.RequestServices.GetService<IProblemDetailsCreator>()
            ?? throw new InvalidOperationException($"{nameof(IProblemDetailsCreator)} service not registered");

    [field: AllowNull, MaybeNull]
    protected IEnumLocaleAccessor LocaleAccessor =>
        field ??=
            HttpContext.RequestServices.GetService<IEnumLocaleAccessor>()
            ?? throw new InvalidOperationException($"{nameof(IEnumLocaleAccessor)} service not registered");

    [NonAction]
    protected ActionResult<EnumLocale<T>[]> LocaleValues<T>()
        where T : struct, Enum
    {
        var result = LocaleAccessor.GetLocale<T>();

        return Ok(result);
    }

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
        var problemDetails = ProblemDetailsCreator.MalformedSyntax();
        ProblemDetailsNormalizer.ApplyProblemDetailsDefaults(HttpContext, problemDetails);

        return base.BadRequest(problemDetails);
    }

    [NonAction]
    protected UnprocessableEntityObjectResult UnprocessableEntityProblemDetails(IEnumerable<ValidationFailure> failures)
    {
        var errors = failures.ToErrorDescriptors();
        var problemDetails = ProblemDetailsCreator.UnprocessableEntity(errors);
        ProblemDetailsNormalizer.ApplyProblemDetailsDefaults(HttpContext, problemDetails);

        return base.UnprocessableEntity(problemDetails);
    }

    [NonAction]
    protected NotFoundObjectResult NotFoundProblemDetails(string entity, string key)
    {
        var problemDetails = ProblemDetailsCreator.EntityNotFound(entity, key);
        ProblemDetailsNormalizer.ApplyProblemDetailsDefaults(HttpContext, problemDetails);

        return base.NotFound(problemDetails);
    }

    [NonAction]
    protected ConflictObjectResult ConflictProblemDetails(IEnumerable<ErrorDescriptor> errorDescriptors)
    {
        var problemDetails = ProblemDetailsCreator.Conflict(errorDescriptors);

        return base.Conflict(problemDetails);
    }

    [NonAction]
    protected ConflictObjectResult ConflictProblemDetails(ErrorDescriptor errorDescriptor)
    {
        var problemDetails = ProblemDetailsCreator.Conflict([errorDescriptor]);
        ProblemDetailsNormalizer.ApplyProblemDetailsDefaults(HttpContext, problemDetails);

        return base.Conflict(problemDetails);
    }
}
