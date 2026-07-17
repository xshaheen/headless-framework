// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.Results;
using Headless.Abstractions;
using Headless.Primitives;
using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Api.Controllers;

/// <summary>
/// Base class for Headless API controllers. Provides lazy-resolved service accessors, convenience
/// action helpers that dispatch through the mediator, and pre-built problem-details action results
/// for common error scenarios.
/// </summary>
[PublicAPI]
[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>
    /// Gets the application configuration, resolved lazily from the request services on first access.
    /// </summary>
    /// <exception cref="InvalidOperationException"><c>IConfiguration</c> is not registered in the DI container.</exception>
    [field: AllowNull, MaybeNull]
    protected IConfiguration Configuration =>
        field ??=
            HttpContext.RequestServices.GetService<IConfiguration>()
            ?? throw new InvalidOperationException($"{nameof(IConfiguration)} service not registered");

    /// <summary>
    /// Gets the Mediator sender, resolved lazily from the request services on first access.
    /// </summary>
    /// <exception cref="InvalidOperationException"><c>ISender</c> is not registered in the DI container.</exception>
    [field: AllowNull, MaybeNull]
    protected ISender Sender =>
        field ??=
            HttpContext.RequestServices.GetService<ISender>()
            ?? throw new InvalidOperationException($"{nameof(ISender)} service not registered");

    /// <summary>Gets the problem-details creator, resolved lazily from the request services on first access.</summary>
    /// <exception cref="InvalidOperationException"><c>IProblemDetailsCreator</c> is not registered in the DI container.</exception>
    [field: AllowNull, MaybeNull]
    private IProblemDetailsCreator ProblemDetailsCreator =>
        field ??=
            HttpContext.RequestServices.GetService<IProblemDetailsCreator>()
            ?? throw new InvalidOperationException($"{nameof(IProblemDetailsCreator)} service not registered");

    /// <summary>
    /// Gets the enum locale accessor, resolved lazily from the request services on first access.
    /// </summary>
    /// <exception cref="InvalidOperationException"><c>IEnumLocaleAccessor</c> is not registered in the DI container.</exception>
    [field: AllowNull, MaybeNull]
    protected IEnumLocaleAccessor LocaleAccessor =>
        field ??=
            HttpContext.RequestServices.GetService<IEnumLocaleAccessor>()
            ?? throw new InvalidOperationException($"{nameof(IEnumLocaleAccessor)} service not registered");

    /// <summary>
    /// Returns a 200 OK response with the localized display values for enum <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The enum type to retrieve locale values for.</typeparam>
    /// <returns>200 OK with the <see cref="EnumLocale{T}"/> array.</returns>
    /// <exception cref="InvalidOperationException"><c>IEnumLocaleAccessor</c> is not registered in the DI container.</exception>
    [NonAction]
    protected ActionResult<EnumLocale<T>[]> LocaleValues<T>()
        where T : struct, Enum
    {
        var result = LocaleAccessor.GetLocale<T>();

        return Ok(result);
    }

    /// <summary>
    /// Dispatches <paramref name="req"/> through the mediator and returns 204 No Content on success.
    /// Returns 400 Bad Request when <paramref name="req"/> is <see langword="null"/>.
    /// </summary>
    /// <param name="req">The command to send, or <see langword="null"/> when binding failed.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>204 No Content or 400 Bad Request.</returns>
    /// <exception cref="InvalidOperationException"><c>ISender</c> or <c>IProblemDetailsCreator</c> is not registered.</exception>
    [NonAction]
    protected async Task<ActionResult> NoContent(IRequest? req, CancellationToken token = default)
    {
        if (req is null)
        {
            return MalformedSyntax();
        }

        await Sender.Send(req, token).ConfigureAwait(false);

        return NoContent();
    }

    /// <summary>
    /// Dispatches <paramref name="req"/> through the mediator and returns 200 OK with the result on success.
    /// Returns 400 Bad Request when <paramref name="req"/> is <see langword="null"/>.
    /// </summary>
    /// <typeparam name="T">The response type.</typeparam>
    /// <param name="req">The query to send, or <see langword="null"/> when binding failed.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>200 OK with <typeparamref name="T"/> or 400 Bad Request.</returns>
    /// <exception cref="InvalidOperationException"><c>ISender</c> or <c>IProblemDetailsCreator</c> is not registered.</exception>
    [NonAction]
    protected async Task<ActionResult<T>> Ok<T>(IRequest<T>? req, CancellationToken token = default)
    {
        return req is null ? MalformedSyntax() : Ok(await Sender.Send(req, token).ConfigureAwait(false));
    }

    /// <summary>
    /// Dispatches <paramref name="req"/> through the mediator and returns 200 OK on success.
    /// Returns 400 Bad Request when <paramref name="req"/> is <see langword="null"/>.
    /// </summary>
    /// <param name="req">The command to send, or <see langword="null"/> when binding failed.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>200 OK or 400 Bad Request.</returns>
    /// <exception cref="InvalidOperationException"><c>ISender</c> or <c>IProblemDetailsCreator</c> is not registered.</exception>
    [NonAction]
    protected async Task<ActionResult> Ok(IRequest? req, CancellationToken token = default)
    {
        if (req is null)
        {
            return MalformedSyntax();
        }

        await Sender.Send(req, token).ConfigureAwait(false);

        return Ok();
    }

    /// <summary>
    /// Returns 200 OK. Convenience overload so that <c>Unit</c>-returning mediator results can be
    /// passed directly without discarding the value explicitly.
    /// </summary>
    /// <param name="_">The <c>Unit</c> result (ignored).</param>
    /// <returns>200 OK.</returns>
    [NonAction]
    protected OkResult Ok(Unit _)
    {
        return Ok();
    }

    /// <summary>
    /// Returns a 400 Bad Request problem-details response indicating malformed or unparseable syntax.
    /// </summary>
    /// <returns>400 Bad Request with a problem-details body.</returns>
    /// <exception cref="InvalidOperationException"><c>IProblemDetailsCreator</c> is not registered in the DI container.</exception>
    [NonAction]
    protected BadRequestObjectResult MalformedSyntax()
    {
        return base.BadRequest(ProblemDetailsCreator.BadRequest());
    }

    /// <summary>
    /// Returns a 422 Unprocessable Entity problem-details response populated with the given validation failures.
    /// </summary>
    /// <param name="failures">The FluentValidation failures to include in the problem body.</param>
    /// <returns>422 Unprocessable Entity with a problem-details body.</returns>
    /// <exception cref="InvalidOperationException"><c>IProblemDetailsCreator</c> is not registered in the DI container.</exception>
    [NonAction]
    protected UnprocessableEntityObjectResult UnprocessableEntityProblemDetails(IEnumerable<ValidationFailure> failures)
    {
        return base.UnprocessableEntity(ProblemDetailsCreator.UnprocessableEntity(failures.ToErrorDescriptors()));
    }

    /// <summary>Returns a 404 Not Found problem-details response.</summary>
    /// <returns>404 Not Found with a problem-details body.</returns>
    /// <exception cref="InvalidOperationException"><c>IProblemDetailsCreator</c> is not registered in the DI container.</exception>
    [NonAction]
    protected NotFoundObjectResult NotFoundProblemDetails()
    {
        return base.NotFound(ProblemDetailsCreator.EntityNotFound());
    }

    /// <summary>Returns a 409 Conflict problem-details response with multiple error descriptors.</summary>
    /// <param name="errorDescriptors">The error descriptors to include in the problem body.</param>
    /// <returns>409 Conflict with a problem-details body.</returns>
    /// <exception cref="InvalidOperationException"><c>IProblemDetailsCreator</c> is not registered in the DI container.</exception>
    [NonAction]
    protected ConflictObjectResult ConflictProblemDetails(IReadOnlyCollection<ErrorDescriptor> errorDescriptors)
    {
        return base.Conflict(ProblemDetailsCreator.Conflict(errorDescriptors));
    }

    /// <summary>Returns a 409 Conflict problem-details response with a single error descriptor.</summary>
    /// <param name="errorDescriptor">The error descriptor to include in the problem body.</param>
    /// <returns>409 Conflict with a problem-details body.</returns>
    /// <exception cref="InvalidOperationException"><c>IProblemDetailsCreator</c> is not registered in the DI container.</exception>
    [NonAction]
    protected ConflictObjectResult ConflictProblemDetails(ErrorDescriptor errorDescriptor)
    {
        return base.Conflict(ProblemDetailsCreator.Conflict([errorDescriptor]));
    }
}
