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
/// Executes a valued <see cref="ApiResult{T}"/> as 200 OK, or as the error's problem-details response, and
/// publishes both response types to endpoint metadata.
/// </summary>
/// <typeparam name="T">The success value type.</typeparam>
/// <remarks>
/// The problem body is built when the result executes, from the request's <see cref="IProblemDetailsCreator"/>, so
/// endpoints never inject the creator. The status is known up front; <see cref="IValueHttpResult.Value"/> is the success
/// value, and <see langword="null"/> for a failure.
/// </remarks>
[PublicAPI]
public sealed class ApiResultHttpResult<T> : IResult, IEndpointMetadataProvider, IStatusCodeHttpResult, IValueHttpResult
{
    private readonly T? _value;
    private readonly ApiResultError? _error;

    internal ApiResultHttpResult(ApiResult<T> result)
    {
        if (result.TryGetValue(out var value))
        {
            _value = value;
        }
        else
        {
            // Error throws for a default(ApiResult<T>), so a result that was never assigned fails here, not mid-response.
            _error = result.Error;
        }
    }

    internal ApiResultHttpResult(ApiResultError error)
    {
        _error = error;
    }

    int? IStatusCodeHttpResult.StatusCode =>
        _error is null ? StatusCodes.Status200OK : ApiResultProblemResponse.GetStatusCode(_error);

    object? IValueHttpResult.Value => _value;

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">The result is a failure and no <see cref="IProblemDetailsCreator"/> is registered.</exception>
    public Task ExecuteAsync(HttpContext httpContext)
    {
        Argument.IsNotNull(httpContext);

        return _error is null
            ? TypedResults.Ok(_value).ExecuteAsync(httpContext)
            : ApiResultProblemResponse.ExecuteAsync(httpContext, _error);
    }

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
/// Executes a valued <see cref="ApiResult{T}"/> as 201 Created with a <c>Location</c> field, or as the error's
/// problem-details response, and publishes both response types to endpoint metadata.
/// </summary>
/// <typeparam name="T">The created resource type.</typeparam>
/// <remarks>
/// The problem body is built when the result executes, from the request's <see cref="IProblemDetailsCreator"/>.
/// <see cref="IValueHttpResult.Value"/> is the created value, and <see langword="null"/> for a failure.
/// </remarks>
[PublicAPI]
public sealed class ApiResultCreatedHttpResult<T>
    : IResult,
        IEndpointMetadataProvider,
        IStatusCodeHttpResult,
        IValueHttpResult
{
    private readonly T? _value;
    private readonly ApiResultError? _error;

    internal ApiResultCreatedHttpResult(ApiResult<T> result, Func<T, string> location)
    {
        Argument.IsNotNull(location);

        if (result.TryGetValue(out var value))
        {
            _value = value;
            Location = location(value);
        }
        else
        {
            _error = result.Error;
        }
    }

    internal ApiResultCreatedHttpResult(ApiResultError error)
    {
        _error = error;
    }

    /// <summary>The <c>Location</c> of the created resource, or <see langword="null"/> for a failure.</summary>
    public string? Location { get; }

    int? IStatusCodeHttpResult.StatusCode =>
        _error is null ? StatusCodes.Status201Created : ApiResultProblemResponse.GetStatusCode(_error);

    object? IValueHttpResult.Value => _value;

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">The result is a failure and no <see cref="IProblemDetailsCreator"/> is registered.</exception>
    public Task ExecuteAsync(HttpContext httpContext)
    {
        Argument.IsNotNull(httpContext);

        return _error is null
            ? TypedResults.Created(Location, _value).ExecuteAsync(httpContext)
            : ApiResultProblemResponse.ExecuteAsync(httpContext, _error);
    }

    /// <summary>Publishes the complete HTTP response contract for Minimal API OpenAPI discovery.</summary>
    /// <param name="method">The endpoint method that returns this result.</param>
    /// <param name="builder">The endpoint builder receiving response metadata.</param>
    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        Argument.IsNotNull(method);
        Argument.IsNotNull(builder);
        ApiResultResponseMetadata.Populate(builder, StatusCodes.Status201Created, typeof(T));
    }
}

/// <summary>
/// Executes a valued <see cref="ApiResult{T}"/> as 202 Accepted with the value, and a <c>Location</c> field when one
/// was given, or as the error's problem-details response, and publishes both response types to endpoint metadata.
/// </summary>
/// <typeparam name="T">The accepted value type, typically a status or tracking resource.</typeparam>
/// <remarks>
/// Use it when the work continues after the response, such as an enqueued job or published message; the value and
/// the <c>Location</c> field tell the client where to follow it. The problem body is built when the result executes,
/// from the request's <see cref="IProblemDetailsCreator"/>. <see cref="IValueHttpResult.Value"/> is the accepted value,
/// and <see langword="null"/> for a failure.
/// </remarks>
[PublicAPI]
public sealed class ApiResultAcceptedHttpResult<T>
    : IResult,
        IEndpointMetadataProvider,
        IStatusCodeHttpResult,
        IValueHttpResult
{
    private readonly T? _value;
    private readonly ApiResultError? _error;

    internal ApiResultAcceptedHttpResult(ApiResult<T> result, Func<T, string>? location)
    {
        if (result.TryGetValue(out var value))
        {
            _value = value;
            Location = location?.Invoke(value);
        }
        else
        {
            _error = result.Error;
        }
    }

    internal ApiResultAcceptedHttpResult(ApiResultError error)
    {
        _error = error;
    }

    /// <summary>
    /// The <c>Location</c> of the status monitor, or <see langword="null"/> for a failure or when none was given.
    /// </summary>
    public string? Location { get; }

    int? IStatusCodeHttpResult.StatusCode =>
        _error is null ? StatusCodes.Status202Accepted : ApiResultProblemResponse.GetStatusCode(_error);

    object? IValueHttpResult.Value => _value;

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">The result is a failure and no <see cref="IProblemDetailsCreator"/> is registered.</exception>
    public Task ExecuteAsync(HttpContext httpContext)
    {
        Argument.IsNotNull(httpContext);

        return _error is null
            ? TypedResults.Accepted(Location, _value).ExecuteAsync(httpContext)
            : ApiResultProblemResponse.ExecuteAsync(httpContext, _error);
    }

    /// <summary>Publishes the complete HTTP response contract for Minimal API OpenAPI discovery.</summary>
    /// <param name="method">The endpoint method that returns this result.</param>
    /// <param name="builder">The endpoint builder receiving response metadata.</param>
    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        Argument.IsNotNull(method);
        Argument.IsNotNull(builder);
        ApiResultResponseMetadata.Populate(builder, StatusCodes.Status202Accepted, typeof(T));
    }
}

/// <summary>
/// Executes a unit <see cref="ApiResult"/> as 204 No Content, or as the error's problem-details response, and
/// publishes both response types to endpoint metadata.
/// </summary>
/// <remarks>
/// The problem body is built when the result executes, from the request's <see cref="IProblemDetailsCreator"/>, so
/// endpoints never inject the creator.
/// </remarks>
[PublicAPI]
public sealed class ApiResultHttpResult : IResult, IEndpointMetadataProvider, IStatusCodeHttpResult
{
    private readonly ApiResultError? _error;

    internal ApiResultHttpResult(ApiResult result)
    {
        if (!result.IsSuccess)
        {
            _error = result.Error;
        }
    }

    internal ApiResultHttpResult(ApiResultError error)
    {
        _error = error;
    }

    int? IStatusCodeHttpResult.StatusCode =>
        _error is null ? StatusCodes.Status204NoContent : ApiResultProblemResponse.GetStatusCode(_error);

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">The result is a failure and no <see cref="IProblemDetailsCreator"/> is registered.</exception>
    public Task ExecuteAsync(HttpContext httpContext)
    {
        Argument.IsNotNull(httpContext);

        return _error is null
            ? TypedResults.NoContent().ExecuteAsync(httpContext)
            : ApiResultProblemResponse.ExecuteAsync(httpContext, _error);
    }

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

/// <summary>
/// Executes a unit <see cref="ApiResult"/> as 202 Accepted without a body, with a <c>Location</c> field when one was
/// given, or as the error's problem-details response, and publishes both response types to endpoint metadata.
/// </summary>
/// <remarks>
/// Use it when the work continues after the response and the client gets nothing to follow but the optional
/// <c>Location</c>. The problem body is built when the result executes, from the request's
/// <see cref="IProblemDetailsCreator"/>.
/// </remarks>
[PublicAPI]
public sealed class ApiResultAcceptedHttpResult : IResult, IEndpointMetadataProvider, IStatusCodeHttpResult
{
    private readonly ApiResultError? _error;

    internal ApiResultAcceptedHttpResult(ApiResult result, string? location)
    {
        if (result.IsSuccess)
        {
            Location = location;
        }
        else
        {
            _error = result.Error;
        }
    }

    internal ApiResultAcceptedHttpResult(ApiResultError error)
    {
        _error = error;
    }

    /// <summary>
    /// The <c>Location</c> of the status monitor, or <see langword="null"/> for a failure or when none was given.
    /// </summary>
    public string? Location { get; }

    int? IStatusCodeHttpResult.StatusCode =>
        _error is null ? StatusCodes.Status202Accepted : ApiResultProblemResponse.GetStatusCode(_error);

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">The result is a failure and no <see cref="IProblemDetailsCreator"/> is registered.</exception>
    public Task ExecuteAsync(HttpContext httpContext)
    {
        Argument.IsNotNull(httpContext);

        return _error is null
            ? TypedResults.Accepted(Location).ExecuteAsync(httpContext)
            : ApiResultProblemResponse.ExecuteAsync(httpContext, _error);
    }

    /// <summary>Publishes the complete HTTP response contract for Minimal API OpenAPI discovery.</summary>
    /// <param name="method">The endpoint method that returns this result.</param>
    /// <param name="builder">The endpoint builder receiving response metadata.</param>
    public static void PopulateMetadata(MethodInfo method, EndpointBuilder builder)
    {
        Argument.IsNotNull(method);
        Argument.IsNotNull(builder);
        ApiResultResponseMetadata.Populate(builder, StatusCodes.Status202Accepted, successType: null);
    }
}
