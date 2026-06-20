// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.Results;
using Headless.Api.Abstractions;
using Headless.Primitives;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

[PublicAPI]
public sealed class MinimalApiValidatorFilter<TRequest> : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var validators = context.HttpContext.RequestServices.GetService<IEnumerable<IValidator<TRequest>>>();

        if (validators is null)
        {
            return await next(context).ConfigureAwait(false);
        }

        // Materialize once: the DI-resolved enumerable is consumed by the emptiness check and the validation
        // loop, so enumerating it twice (Any() + ToList()) would re-run a lazy source.
        var validatorList = validators as IList<IValidator<TRequest>> ?? validators.ToList();

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
