// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.Results;
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
/// services never run concurrently and each failure is reported once; a single validator uses a fast path.
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

        ValidationResult[] validationResults;

        if (validatorList.Count == 1)
        {
            // Fast path for a single validator: the common IsValid case returns without allocating the results
            // array at all.
            var result = await validatorList[0]
                .ValidateAsync(new ValidationContext<TRequest>(request), context.HttpContext.RequestAborted)
                .ConfigureAwait(false);

            if (result.IsValid)
            {
                return await next(context).ConfigureAwait(false);
            }

            validationResults = [result];
        }
        else
        {
            // Sequential, one context per validator: a FluentValidation result wraps its context's failure list, so a
            // shared context would hand every validator's failures to every result (and race when run concurrently),
            // and validators commonly share scoped services such as a DbContext that are not safe for concurrent use.
            validationResults = new ValidationResult[validatorList.Count];

            for (var i = 0; i < validatorList.Count; i++)
            {
                validationResults[i] = await validatorList[i]
                    .ValidateAsync(new ValidationContext<TRequest>(request), context.HttpContext.RequestAborted)
                    .ConfigureAwait(false);
            }
        }

        // Early exit if all valid - avoids the failure-projection LINQ chain below
        if (validationResults.All(x => x.IsValid))
        {
            return await next(context).ConfigureAwait(false);
        }

        var failures = validationResults
            .Where(x => !x.IsValid)
            .SelectMany(result => result.Errors)
            .Where(failure => failure is not null)
            .ToErrorDescriptors();

        var problemDetailsCreator = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsCreator>();

        return TypedResults.Problem(problemDetailsCreator.UnprocessableEntity(failures));
    }
}
