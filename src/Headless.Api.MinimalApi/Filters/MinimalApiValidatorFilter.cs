// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.Results;
using Headless.Api.Abstractions;
using Headless.Primitives;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Endpoint filter that runs all registered <see cref="IValidator{T}"/> implementations for
/// <typeparamref name="TRequest"/> before the endpoint handler is invoked.
/// </summary>
/// <typeparam name="TRequest">The request type to validate.</typeparam>
/// <remarks>
/// <para>
/// If no validators are registered the request is passed through unchanged.
/// Multiple validators run in parallel via <see cref="Task.WhenAll(System.Collections.Generic.IEnumerable{Task})"/>;
/// a single validator uses a fast path that avoids the allocation.
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
        var request = context.Arguments.OfType<TRequest>().FirstOrDefault(request => request?.GetType() == requestType);

        if (request is null)
        {
            var creator = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsCreator>();

            return TypedResults.Problem(
                creator.BadRequest(
                    error: new ErrorDescriptor(
                        "g:invalid_request_type",
                        "Invalid request type configured for this endpoint."
                    )
                )
            );
        }

        var validationContext = new ValidationContext<TRequest>(request);

        ValidationResult[] validationResults;

        if (validatorList.Count == 1)
        {
            // Fast path for single validator - avoids Task.WhenAll overhead
            var result = await validatorList[0]
                .ValidateAsync(validationContext, context.HttpContext.RequestAborted)
                .ConfigureAwait(false);
            validationResults = [result];
        }
        else
        {
            // Parallel path for multiple validators
            validationResults = await Task.WhenAll(
                    validatorList.Select(v => v.ValidateAsync(validationContext, context.HttpContext.RequestAborted))
                )
                .ConfigureAwait(false);
        }

        // Early exit if all valid - avoid LINQ chain and dictionary allocation
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
