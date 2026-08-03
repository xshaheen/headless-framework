// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Abstractions;
using Headless.Checks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
#pragma warning disable CA1000 // IEndpointMetadataProvider requires a static member on the returned generic type.
#pragma warning disable RCS1158 // The metadata member is required by IEndpointMetadataProvider.
namespace Headless.Primitives;

/// <summary>
/// Executes a valued <see cref="ApiResult{T}"/> and publishes its success and ProblemDetails response
/// types to endpoint metadata.
/// </summary>
/// <typeparam name="T">The success value type.</typeparam>
[PublicAPI]
public sealed class ApiResultHttpResult<T> : IResult, IEndpointMetadataProvider, IStatusCodeHttpResult
{
    private readonly IResult _result;

    internal ApiResultHttpResult(ApiResult<T> result, IProblemDetailsCreator creator)
    {
        Argument.IsNotNull(creator);
        _result = result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToHttpResult(creator);
    }

    int? IStatusCodeHttpResult.StatusCode => (_result as IStatusCodeHttpResult)?.StatusCode;

    /// <inheritdoc/>
    public Task ExecuteAsync(HttpContext httpContext) => _result.ExecuteAsync(httpContext);

    /// <summary>Publishes the complete HTTP response contract for Minimal API OpenAPI discovery.</summary>
    /// <param name="method">The endpoint method that returns this result.</param>
    /// <param name="builder">The endpoint builder receiving response metadata.</param>
    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        Argument.IsNotNull(method);
        Argument.IsNotNull(builder);
        ApiResultHttpResultMetadata.Populate(builder, StatusCodes.Status200OK, typeof(T));
    }
}

/// <summary>
/// Executes a unit <see cref="ApiResult"/> and publishes its success and ProblemDetails response types
/// to endpoint metadata.
/// </summary>
[PublicAPI]
public sealed class ApiResultHttpResult : IResult, IEndpointMetadataProvider, IStatusCodeHttpResult
{
    private readonly IResult _result;

    internal ApiResultHttpResult(ApiResult result, IProblemDetailsCreator creator)
    {
        Argument.IsNotNull(creator);
        _result = result.IsSuccess ? TypedResults.NoContent() : result.Error.ToHttpResult(creator);
    }

    int? IStatusCodeHttpResult.StatusCode => (_result as IStatusCodeHttpResult)?.StatusCode;

    /// <inheritdoc/>
    public Task ExecuteAsync(HttpContext httpContext) => _result.ExecuteAsync(httpContext);

    /// <summary>Publishes the complete HTTP response contract for Minimal API OpenAPI discovery.</summary>
    /// <param name="method">The endpoint method that returns this result.</param>
    /// <param name="builder">The endpoint builder receiving response metadata.</param>
    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        Argument.IsNotNull(method);
        Argument.IsNotNull(builder);
        ApiResultHttpResultMetadata.Populate(builder, StatusCodes.Status204NoContent, successType: null);
    }
}

file static class ApiResultHttpResultMetadata
{
    private static readonly int[] _ProblemStatusCodes =
    [
        StatusCodes.Status401Unauthorized,
        StatusCodes.Status403Forbidden,
        StatusCodes.Status404NotFound,
        StatusCodes.Status409Conflict,
        StatusCodes.Status422UnprocessableEntity,
    ];

    public static void Populate(EndpointBuilder builder, int successStatusCode, Type? successType)
    {
        builder.Metadata.Add(
            new ProducesResponseTypeMetadata(
                successStatusCode,
                successType,
                successType is null ? [] : ["application/json"]
            )
        );

        foreach (var statusCode in _ProblemStatusCodes)
        {
            builder.Metadata.Add(
                new ProducesResponseTypeMetadata(statusCode, typeof(ProblemDetails), ["application/problem+json"])
            );
        }
    }
}
