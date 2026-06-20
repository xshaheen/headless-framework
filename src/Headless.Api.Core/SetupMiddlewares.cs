// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.Api.Middlewares;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Api;

/// <summary>
/// Allows higher-level packages (e.g. <c>Headless.Api.ServiceDefaults</c>) to receive a callback when
/// <see cref="SetupMiddlewares.UseStatusCodesRewriter"/> is wired into the pipeline, without creating a
/// circular package dependency.
/// </summary>
/// <remarks>Framework coordination interface — not intended for direct consumer use.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IStatusCodesRewriterCalledNotifier
{
    /// <summary>Called immediately when <see cref="SetupMiddlewares.UseStatusCodesRewriter"/> is added to the pipeline.</summary>
    void OnCalled();
}

[PublicAPI]
public static class SetupMiddlewares
{
    /// <summary>
    /// Registers <c>ServerTimingMiddleware</c> as a singleton in the DI container.
    /// Call <see cref="UseServerTiming"/> after this to add it to the pipeline.
    /// </summary>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddServerTimingMiddleware(this IServiceCollection services)
    {
        services.TryAddSingleton<ServerTimingMiddleware>();
        return services;
    }

    /// <summary>
    /// Adds the server-timing middleware to the pipeline. It measures end-to-end request processing
    /// time and appends a <c>Server-Timing</c> trailer header so browser DevTools can surface the
    /// duration. Only appended when the response supports trailers; silently no-ops otherwise.
    /// </summary>
    /// <param name="application">The application builder.</param>
    /// <returns>The same application builder.</returns>
    public static IApplicationBuilder UseServerTiming(this IApplicationBuilder application)
    {
        return application.UseMiddleware<ServerTimingMiddleware>();
    }

    /// <summary>
    /// Adds the no-cache headers middleware to the pipeline. When the response completes without
    /// an explicit <c>Cache-Control</c> header, the middleware injects
    /// <c>Cache-Control: no-cache,no-store,must-revalidate</c>.
    /// </summary>
    /// <param name="application">The application builder.</param>
    /// <returns>The same application builder.</returns>
    public static IApplicationBuilder UseNoCacheWhenMissingCacheHeaders(this IApplicationBuilder application)
    {
        return application.UseMiddleware<NoCacheHeadersMiddleware>();
    }

    /// <summary>
    /// Registers <c>StatusCodesRewriterMiddleware</c> as a singleton in the DI container.
    /// Call <see cref="UseStatusCodesRewriter"/> after this to add it to the pipeline.
    /// </summary>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddStatusCodesRewriterMiddleware(this IServiceCollection services)
    {
        services.TryAddSingleton<StatusCodesRewriterMiddleware>();
        return services;
    }

    /// <summary>
    /// Adds the status-codes rewriter middleware to the ASP.NET Core request pipeline.
    /// It intercepts bare 401, 403, and 404 responses (without an existing body) and rewrites them
    /// as structured <c>application/problem+json</c> responses via <see cref="Headless.Api.Abstractions.IProblemDetailsCreator"/>.
    /// For 403 responses that carry a <c>TenantContextRequiredFeature</c> marker, it substitutes the
    /// <c>g:tenant_required</c> ProblemDetails body regardless of any upstream
    /// <c>Content-Type</c> already set.
    /// </summary>
    /// <remarks>
    /// Notifies any registered <see cref="IStatusCodesRewriterCalledNotifier"/> (e.g.,
    /// <c>HeadlessServiceDefaultsValidationStartupFilter</c>) synchronously before adding the middleware.
    /// </remarks>
    /// <returns>The same application builder.</returns>
    public static IApplicationBuilder UseStatusCodesRewriter(this IApplicationBuilder app)
    {
        // Notify any registered observer (e.g. HeadlessServiceDefaultsValidationStartupFilter) that the middleware was wired.
        if (
            app.ApplicationServices.GetService(typeof(IStatusCodesRewriterCalledNotifier))
            is IStatusCodesRewriterCalledNotifier notifier
        )
        {
            notifier.OnCalled();
        }

        return app.UseMiddleware<StatusCodesRewriterMiddleware>();
    }

    /// <summary>
    /// Registers <c>TenantResolutionMiddleware</c> as a singleton in the DI container.
    /// Call <see cref="UseTenantResolution"/> (or <see cref="SetupApiTenancy.UseHeadlessTenancy"/>)
    /// after this to add it to the pipeline.
    /// </summary>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddTenantResolution(this IServiceCollection services)
    {
        services.TryAddSingleton<TenantResolutionMiddleware>();
        return services;
    }

    /// <summary>
    /// Adds <c>TenantResolutionMiddleware</c> to the pipeline. It reads the configured tenant claim
    /// from the authenticated principal and sets <see cref="Headless.Abstractions.ICurrentTenant"/>
    /// for the duration of the request. Endpoints decorated with <see cref="MultiTenancy.SkipTenantResolutionAttribute"/>
    /// are bypassed entirely. Unauthenticated requests are passed through without setting a tenant.
    /// </summary>
    /// <param name="application">The application builder.</param>
    /// <returns>The same application builder.</returns>
    /// <remarks>
    /// Place this after <c>UseAuthentication()</c> and before <c>UseAuthorization()</c>.
    /// A one-time process-level warning is emitted when the middleware observes a request that
    /// has not been processed by <c>AuthenticationMiddleware</c> (likely ordering misconfiguration).
    /// Prefer <see cref="SetupApiTenancy.UseHeadlessTenancy"/> when HTTP tenancy was configured
    /// through the tenancy builder — it guards against double-registration and validates the setup.
    /// </remarks>
    public static IApplicationBuilder UseTenantResolution(this IApplicationBuilder application)
    {
        return application.UseMiddleware<TenantResolutionMiddleware>();
    }
}
