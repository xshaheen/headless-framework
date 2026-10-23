// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Api;

/// <summary>
/// The one mapping from an <see cref="ApiResultError"/> to its problem-details response, shared by the Minimal API and
/// MVC result conversions, so the two never disagree on which status an error kind gets.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="NotFoundError"/> 404, <see cref="ValidationError"/> 422, <see cref="ForbiddenError"/> 403,
/// <see cref="UnauthorizedError"/> 401, an <see cref="AggregateError"/> of only validation errors 422, any other
/// <see cref="AggregateError"/> 409, <see cref="ConflictError"/> 409, an error the application mapped through
/// <see cref="HeadlessProblemDetailsOptions.MapError{TError}"/> its mapped status, and every other error 409. The
/// status is the returned <see cref="ProblemDetails.Status"/>.
/// </para>
/// <para>
/// The per-kind builders (<see cref="NotFound"/>, <see cref="Unauthorized"/>, <see cref="Forbidden"/>,
/// <see cref="Conflict"/>, <see cref="Validation"/>, <see cref="Mapped"/>) are also what the exception handler calls
/// for the exception of the same kind, so a returned error and a thrown exception that mean the same failure write
/// the same body.
/// </para>
/// </remarks>
internal static class ApiResultErrorProblemDetails
{
    public static ProblemDetails Create(
        ApiResultError error,
        IProblemDetailsCreator creator,
        HeadlessProblemDetailsOptions? mappings
    )
    {
        return error switch
        {
            NotFoundError => NotFound(creator),
            ValidationError e => Validation(creator, e.Errors),
            ForbiddenError e => Forbidden(creator, e.Error),
            UnauthorizedError e => Unauthorized(creator, e.Error),
            AggregateError e when e.TryGetValidationErrors(out var validationErrors) => Validation(
                creator,
                validationErrors
            ),
            AggregateError e => Conflict(creator, e.ToErrorDescriptors()),
            ConflictError e => Conflict(creator, e.Errors),
            _ when mappings?.TryGetStatusCode(error, out var statusCode) == true => Mapped(
                creator,
                statusCode,
                error.ToErrorDescriptor()
            ),
            _ => Conflict(creator, [error.ToErrorDescriptor()]),
        };
    }

    /// <summary>The application's mappings registered in <paramref name="services"/>, if any.</summary>
    public static HeadlessProblemDetailsOptions? GetMappings(IServiceProvider? services) =>
        services?.GetService<IOptions<HeadlessProblemDetailsOptions>>()?.Value;

    /// <summary>The 404 body for a missing entity, whether it was returned as an error or thrown.</summary>
    public static ProblemDetails NotFound(IProblemDetailsCreator creator) => creator.EntityNotFound();

    /// <summary>The 401 body; a carried reason becomes the <c>detail</c>.</summary>
    public static ProblemDetails Unauthorized(IProblemDetailsCreator creator, ErrorDescriptor? error) =>
        _UseErrorAsDetail(creator.Unauthorized(error: error));

    /// <summary>The 403 body; the reason becomes the <c>detail</c>.</summary>
    public static ProblemDetails Forbidden(IProblemDetailsCreator creator, ErrorDescriptor error) =>
        _UseErrorAsDetail(creator.Forbidden(error: error));

    /// <summary>
    /// The body of a failure the application mapped to <paramref name="statusCode"/>: the standard fields, plus the
    /// descriptor and its localized description as the <c>detail</c> when there is one.
    /// </summary>
    public static ProblemDetails Mapped(IProblemDetailsCreator creator, int statusCode, ErrorDescriptor? error)
    {
        var problemDetails = new ProblemDetails { Status = statusCode };

        if (error is not null)
        {
            problemDetails.Extensions["error"] = error;
        }

        creator.Normalize(problemDetails);

        return _UseErrorAsDetail(problemDetails);
    }

    /// <summary>The 409 body listing every conflicting condition.</summary>
    public static ProblemDetails Conflict(
        IProblemDetailsCreator creator,
        IReadOnlyCollection<ErrorDescriptor> errors
    ) => creator.Conflict(errors);

    /// <summary>The 422 body listing every field error.</summary>
    public static ProblemDetails Validation(
        IProblemDetailsCreator creator,
        IReadOnlyDictionary<string, IReadOnlyList<ErrorDescriptor>> errors
    ) => creator.UnprocessableEntity(errors);

    /// <summary>
    /// The status <see cref="Create"/> gives <paramref name="error"/>, known without building the body, so a result
    /// can report its status before a request supplies the <see cref="IProblemDetailsCreator"/>.
    /// </summary>
    /// <returns>
    /// The status, or <see langword="null"/> for an error the framework does not know when
    /// <paramref name="mappings"/> is <see langword="null"/>: the application may map it, and only the request's
    /// services can say.
    /// </returns>
    public static int? GetStatusCode(ApiResultError error, HeadlessProblemDetailsOptions? mappings)
    {
        return error switch
        {
            NotFoundError => StatusCodes.Status404NotFound,
            ValidationError => StatusCodes.Status422UnprocessableEntity,
            ForbiddenError => StatusCodes.Status403Forbidden,
            UnauthorizedError => StatusCodes.Status401Unauthorized,
            AggregateError e when e.TryGetValidationErrors(out _) => StatusCodes.Status422UnprocessableEntity,
            AggregateError or ConflictError => StatusCodes.Status409Conflict,
            _ when mappings is null => null,
            _ when mappings.TryGetStatusCode(error, out var statusCode) => statusCode,
            _ => StatusCodes.Status409Conflict,
        };
    }

    // A denial that names its reason should say it in `detail`, the field clients show, instead of the generic
    // "forbidden" text. The descriptor is read back from the extension after Normalize so the detail carries the
    // localized description, not the one the handler wrote.
    private static ProblemDetails _UseErrorAsDetail(ProblemDetails problemDetails)
    {
        if (problemDetails.Extensions.TryGetValue("error", out var value) && value is ErrorDescriptor descriptor)
        {
            problemDetails.Detail = descriptor.Description;
        }

        return problemDetails;
    }
}
