// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Checks;
using Headless.Primitives;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;

#pragma warning disable CA1000 // IEndpointMetadataProvider requires a static member on the returned generic type.
namespace Headless.Api;

/// <summary>
/// Executes a valued <see cref="ApiResult{T}"/> and publishes its success and ProblemDetails response
/// types to endpoint metadata.
/// </summary>
/// <typeparam name="T">The success value type.</typeparam>
[PublicAPI]
public sealed class ApiResultHttpResult<T> : IResult, IEndpointMetadataProvider, IStatusCodeHttpResult, IValueHttpResult
{
    private readonly IResult _result;

    internal ApiResultHttpResult(ApiResult<T> result, IProblemDetailsCreator creator)
    {
        Argument.IsNotNull(creator);
        _result = result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToHttpResult(creator);
    }

    int? IStatusCodeHttpResult.StatusCode => (_result as IStatusCodeHttpResult)?.StatusCode;

    object? IValueHttpResult.Value => (_result as IValueHttpResult)?.Value;

    /// <inheritdoc/>
    public Task ExecuteAsync(HttpContext httpContext) => _result.ExecuteAsync(httpContext);

    /// <summary>Publishes the complete HTTP response contract for Minimal API OpenAPI discovery.</summary>
    /// <param name="method">The endpoint method that returns this result.</param>
    /// <param name="builder">The endpoint builder receiving response metadata.</param>
    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        Argument.IsNotNull(method);
        Argument.IsNotNull(builder);
        ApiResultResponseMetadata.Populate(builder, StatusCodes.Status200OK, typeof(T));
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
        ApiResultResponseMetadata.Populate(builder, StatusCodes.Status204NoContent, successType: null);
    }
}
