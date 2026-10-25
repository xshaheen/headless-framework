// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Api;

/// <summary>The validation step the Minimal API validation filters share: run validators, answer 422.</summary>
internal static class EndpointValidation
{
    /// <inheritdoc cref="ArgumentValidation.CollectFailuresAsync{T}"/>
    public static ValueTask<List<ValidationFailure>?> CollectFailuresAsync<T>(
        IEnumerable<IValidator<T>> validators,
        T instance,
        List<ValidationFailure>? failures,
        CancellationToken cancellationToken
    )
    {
        return ArgumentValidation.CollectFailuresAsync(validators, instance, failures, cancellationToken);
    }

    /// <summary>Builds the 422 problem response for <paramref name="failures"/> with the request's creator.</summary>
    public static ProblemHttpResult UnprocessableEntity(
        HttpContext httpContext,
        IEnumerable<ValidationFailure> failures
    )
    {
        var creator = httpContext.RequestServices.GetRequiredService<IProblemDetailsCreator>();

        return TypedResults.Problem(creator.UnprocessableEntity(failures.ToErrorDescriptors()));
    }
}
