// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Framework.Api.Abstractions;
using Framework.Primitives;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Framework.Api.MinimalApi.Endpoints;

[PublicAPI]
public static class EndpointRouteBuilderExtensions
{
    public static RouteHandlerBuilder Map<TRequest, TResponse>(
        this IEndpointRouteBuilder endpoints,
        [StringSyntax("Route")] string pattern
    )
        where TRequest : IRequest<TResponse>
    {
        static async Task<Result<Ok<TResponse>, ProblemHttpResult>> handler(
            TRequest? request,
            ISender sender,
            IProblemDetailsCreator problemDetailsCreator
        )
        {
            return request is null
                ? TypedResults.Problem(problemDetailsCreator.MalformedSyntax())
                : TypedResults.Ok(await sender.Send(request));
        }

        return endpoints.Map(pattern, handler).Validate<TRequest>();
    }

    public static RouteHandlerBuilder MapGet<TRequest, TResponse>(
        this IEndpointRouteBuilder endpoints,
        [StringSyntax("Route")] string pattern
    )
        where TRequest : IRequest<TResponse>
    {
        static async Task<Result<Ok<TResponse>, ProblemHttpResult>> handler(
            TRequest? request,
            ISender sender,
            IProblemDetailsCreator problemDetailsCreator
        )
        {
            return request is null
                ? TypedResults.Problem(problemDetailsCreator.MalformedSyntax())
                : TypedResults.Ok(await sender.Send(request));
        }

        return endpoints.MapGet(pattern, handler).Validate<TRequest>();
    }

    public static RouteHandlerBuilder MapPost<TRequest, TResponse>(
        this IEndpointRouteBuilder endpoints,
        [StringSyntax("Route")] string pattern
    )
        where TRequest : IRequest<TResponse>
    {
        static async Task<Result<Ok<TResponse>, ProblemHttpResult>> handler(
            TRequest? request,
            ISender sender,
            IProblemDetailsCreator problemDetailsCreator
        )
        {
            return request is null
                ? TypedResults.Problem(problemDetailsCreator.MalformedSyntax())
                : TypedResults.Ok(await sender.Send(request));
        }

        return endpoints.MapPost(pattern, handler).Validate<TRequest>();
    }

    public static RouteHandlerBuilder MapPut<TRequest, TResponse>(
        this IEndpointRouteBuilder endpoints,
        [StringSyntax("Route")] string pattern
    )
        where TRequest : IRequest<TResponse>
    {
        static async Task<Result<Ok<TResponse>, ProblemHttpResult>> handler(
            TRequest? request,
            ISender sender,
            IProblemDetailsCreator problemDetailsCreator
        )
        {
            return request is null
                ? TypedResults.Problem(problemDetailsCreator.MalformedSyntax())
                : TypedResults.Ok(await sender.Send(request));
        }

        return endpoints.MapPost(pattern, handler).Validate<TRequest>();
    }

    public static RouteHandlerBuilder MapDelete<TRequest, TResponse>(
        this IEndpointRouteBuilder endpoints,
        [StringSyntax("Route")] string pattern
    )
        where TRequest : IRequest<TResponse>
    {
        static async Task<Result<Ok<TResponse>, ProblemHttpResult>> handler(
            TRequest? request,
            ISender sender,
            IProblemDetailsCreator problemDetailsCreator
        )
        {
            return request is null
                ? TypedResults.Problem(problemDetailsCreator.MalformedSyntax())
                : TypedResults.Ok(await sender.Send(request));
        }

        return endpoints.MapDelete(pattern, handler).Validate<TRequest>();
    }
}
