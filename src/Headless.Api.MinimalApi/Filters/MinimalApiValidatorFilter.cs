// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Api.Resources;
using Headless.Primitives;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Api;

/// <summary>
/// Endpoint filter that runs all registered <see cref="IValidator{T}"/> implementations for
/// <typeparamref name="TRequest"/> before the endpoint handler is invoked.
/// </summary>
/// <typeparam name="TRequest">The request type to validate.</typeparam>
/// <remarks>
/// <para>
/// If no validators are registered the request is passed through unchanged.
/// Multiple validators run one after another, each with its own validation context, so validators that share scoped
/// services never run concurrently and each failure is reported once.
/// </para>
/// <para>
/// Validation failures are returned as a 422 Unprocessable Entity problem-details response —
/// the endpoint handler is never invoked. When no argument of type <typeparamref name="TRequest"/>
/// is found in the invocation context, a 400 Bad Request is returned instead.
/// </para>
/// <para>Register via <see cref="RouteBuilderExtensions.Validate{TArgument}"/>.</para>
/// </remarks>
[PublicAPI]
public sealed class MinimalApiValidatorFilter<TRequest> : IEndpointFilter
{
    /// <summary>
    /// Validates the <typeparamref name="TRequest"/> argument and either returns a problem-details
    /// response or invokes <paramref name="next"/>.
    /// </summary>
    /// <param name="context">The endpoint filter invocation context.</param>
    /// <param name="next">The next filter or endpoint handler delegate.</param>
    /// <returns>
    /// A problem-details <see cref="Microsoft.AspNetCore.Http.IResult"/> when validation fails,
    /// otherwise the result from <paramref name="next"/>.
    /// </returns>
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var validators = context.HttpContext.RequestServices.GetService<IEnumerable<IValidator<TRequest>>>();

        if (validators is null)
        {
            return await next(context).ConfigureAwait(false);
        }

        // Materialize once: the DI-resolved enumerable is consumed by the emptiness check and the validation
        // loop, so enumerating it twice (Any() + ToList()) would re-run a lazy source.
        var validatorList = validators.AsIList();

        if (validatorList.Count == 0)
        {
            return await next(context).ConfigureAwait(false);
        }

        var requestType = typeof(TRequest);

        // Manual scan instead of OfType().FirstOrDefault(predicate): avoids two iterator allocations
        // and a closure on every validated request. Exact-type match (not subtype) is intentional.
        TRequest? request = default;

        foreach (var argument in context.Arguments)
        {
            if (argument is TRequest candidate && candidate.GetType() == requestType)
            {
                request = candidate;
                break;
            }
        }

        if (request is null)
        {
            var creator = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsCreator>();

            var error = GeneralMessageDescriber.InvalidRequestType();

            // The body is not at fault here, so the generic bad-request detail would contradict the error.
            return TypedResults.Problem(creator.BadRequest(detail: error.Description, error: error));
        }

        var failures = await ArgumentValidation
            .CollectFailuresAsync(validatorList, request, failures: null, context.HttpContext.RequestAborted)
            .ConfigureAwait(false);

        return failures is null
            ? await next(context).ConfigureAwait(false)
            : EndpointValidation.UnprocessableEntity(context.HttpContext, failures);
    }
}
