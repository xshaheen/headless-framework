// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Api.Middlewares;

public static class AddMiddlewareExtensions
{
    /// <summary>Adds the idempotency middleware.</summary>
    public static IServiceCollection AddIdempotencyMiddleware(
        this IServiceCollection services,
        Action<IdempotencyMiddlewareOptions>? setupAction
    )
    {
        services.Configure<IdempotencyMiddlewareOptions, IdempotencyMiddlewareOptionsValidator>(setupAction);

        return services.AddSingleton<IdempotencyMiddleware>();
    }

    /// <summary>Adds the idempotency middleware.</summary>
    public static IServiceCollection AddIdempotencyMiddleware(
        this IServiceCollection services,
        Action<IdempotencyMiddlewareOptions, IServiceProvider>? setupAction
    )
    {
        services.Configure<IdempotencyMiddlewareOptions, IdempotencyMiddlewareOptionsValidator>(setupAction);

        return services.AddSingleton<IdempotencyMiddleware>();
    }

    /// <summary>Adds the server timing middleware.</summary>
    public static IServiceCollection AddServerTimingMiddleware(this IServiceCollection services)
    {
        return services.AddSingleton<ServerTimingMiddleware>();
    }

    /// <summary>
    /// Measures the time the request takes to process and returns this in a Server-Timing trailing HTTP header.
    /// It is used to surface any back-end server timing metrics (e.g. database read/write, CPU time, file system
    /// access, etc.) to the developer tools in the user's browser.
    /// </summary>
    public static IApplicationBuilder UseServerTiming(this IApplicationBuilder application)
    {
        return application.UseMiddleware<ServerTimingMiddleware>();
    }

    /// <summary>This is a custom middleware that rewrites the status code of the response.</summary>
    public static IServiceCollection AddStatusCodesRewriterMiddleware(this IServiceCollection services)
    {
        return services.AddSingleton<StatusCodesRewriterMiddleware>();
    }

    /// <summary>
    /// Add the status codes rewriter middleware to the pipeline to rewrite the endpoint not found status code as problem details response.
    /// When request URL does not match any route, status code 404 is returned with a problem details response.
    /// </summary>
    public static IApplicationBuilder UseStatusCodesRewriter(this IApplicationBuilder app)
    {
        return app.UseMiddleware<StatusCodesRewriterMiddleware>();
    }

    /// <summary>Adds middleware that resolves the current tenant from authenticated user claims.</summary>
    public static IServiceCollection AddTenantResolution(this IServiceCollection services)
    {
        return services.AddSingleton<TenantResolutionMiddleware>();
    }

    /// <summary>
    /// Resolves the current tenant from the authenticated user's claims for the lifetime of the HTTP request.
    /// Register this after <c>UseAuthentication()</c> and before <c>UseAuthorization()</c>.
    /// </summary>
    public static IApplicationBuilder UseTenantResolution(this IApplicationBuilder application)
    {
        return application.UseMiddleware<TenantResolutionMiddleware>();
    }
}
