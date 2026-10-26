// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Api;

/// <summary>The 422 response the Minimal API validation filters share.</summary>
internal static class EndpointValidation
{
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
