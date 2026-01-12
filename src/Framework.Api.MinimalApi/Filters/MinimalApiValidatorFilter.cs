// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

[PublicAPI]
public sealed class MinimalApiValidatorFilter<TRequest> : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var validators = context.HttpContext.RequestServices.GetService<IEnumerable<IValidator<TRequest>>>()?.ToList();

        if (validators is null || validators.Count == 0)
        {
            return await next(context).AnyContext();
        }

        var requestType = typeof(TRequest);
        var request = context.Arguments.OfType<TRequest>().FirstOrDefault(request => request?.GetType() == requestType);

        if (request is null)
        {
            return Results.Problem("Invalid request type configured for this endpoint.");
        }

        var validationContext = new ValidationContext<TRequest>(request);

        ValidationResult[] validationResults;

        if (validators.Count == 1)
        {
            // Fast path for single validator - avoids Task.WhenAll overhead
            var result = await validators[0].ValidateAsync(validationContext, context.HttpContext.RequestAborted).AnyContext();
            validationResults = [result];
        }
        else
        {
            // Parallel path for multiple validators
            validationResults = await Task.WhenAll(
                    validators.Select(v => v.ValidateAsync(validationContext, context.HttpContext.RequestAborted))
                )
                .AnyContext();
        }

        var failures = validationResults
            .Where(x => !x.IsValid)
            .SelectMany(result => result.Errors)
            .Where(failure => failure is not null)
            .GroupBy(x => x.PropertyName, x => x.ErrorMessage, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.Ordinal);

        return failures.Count > 0 ? Results.ValidationProblem(failures) : await next(context).AnyContext();
    }
}
