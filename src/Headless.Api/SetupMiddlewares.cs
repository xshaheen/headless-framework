// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using Headless.Context;
using Headless.MultiTenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Api;

[PublicAPI]
public static class SetupMiddlewares
{
    // UseAuthorization() sets this builder property before adding its middleware
    // (Microsoft.AspNetCore.Builder.AuthorizationAppBuilderExtensions); ASP.NET Core keeps the constant
    // internal, so its value is mirrored here. Its presence when the rewriter is added means authorization
    // already sits ahead of it in the pipeline.
    private const string _AuthorizationMiddlewareSetKey = "__AuthorizationMiddlewareSet";

    /// <summary>
    /// Registers <c>ServerTimingMiddleware</c> as a singleton in the DI container.
    /// Call <see cref="UseHeadlessServerTiming"/> after this to add it to the pipeline.
    /// </summary>
    /// <remarks>
    /// Keeps the <c>Headless</c> prefix, unlike the other generic HTTP utilities here: the widely used
    /// <c>Lib.AspNetCore.ServerTiming</c> package already declares <c>AddServerTiming()</c> and <c>UseServerTiming()</c>
    /// in the <c>Microsoft.AspNetCore.Builder</c> namespace, and a host referencing both would hit an ambiguous call.
    /// </remarks>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddHeadlessServerTiming(this IServiceCollection services)
    {
        services.TryAddSingleton<ServerTimingMiddleware>();
        return services;
    }

    /// <summary>
    /// Adds the server-timing middleware to the pipeline. It measures end-to-end request processing
    /// time and appends a <c>Server-Timing</c> trailer header so browser DevTools can surface the
    /// duration. Only appended when the response supports trailers; silently no-ops otherwise.
    /// </summary>
    /// <remarks>
    /// Keeps the <c>Headless</c> prefix, unlike the other generic HTTP utilities here: the widely used
    /// <c>Lib.AspNetCore.ServerTiming</c> package already declares <c>AddServerTiming()</c> and <c>UseServerTiming()</c>
    /// in the <c>Microsoft.AspNetCore.Builder</c> namespace, and a host referencing both would hit an ambiguous call.
    /// </remarks>
    /// <param name="application">The application builder.</param>
    /// <returns>The same application builder.</returns>
    public static IApplicationBuilder UseHeadlessServerTiming(this IApplicationBuilder application)
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
    /// <remarks>
    /// Also wraps the registered <see cref="IAuthorizationEvaluator"/> so an HTTP authorization that failed only on
    /// requirements implementing <c>Headless.Primitives.IDescribedRequirement</c> (such as the feature requirement behind
    /// <c>[RequiresFeature]</c>) is rewritten from the challenge or forbid into the problem response the requirements
    /// describe: 409 <c>g:feature_currently_not_available</c> for a disabled feature. An evaluator registered after this
    /// call replaces the wrapper, and those failures then keep ASP.NET Core's 401 or 403.
    /// It also refuses, with <see cref="InvalidOperationException"/>, to run an endpoint that carries authorization
    /// requirement data (<c>[RequiresFeature]</c>) when the authorization middleware never saw it, as ASP.NET Core
    /// already does for <c>[Authorize]</c>; <c>RouteOptions.SuppressCheckForUnhandledSecurityMetadata</c> turns both off.
    /// </remarks>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddStatusCodesRewriterMiddleware(this IServiceCollection services)
    {
        services.TryAddSingleton<StatusCodesRewriterMiddleware>();

        // The rewriter writes the described error (409 for a disabled feature) for an authorization that failed only on
        // IDescribedRequirements; the evaluator decorator is what hands it that rejection. AddAuthorizationCore
        // registers the default evaluator with TryAdd, so registering it first keeps one registration whichever call
        // runs first and gives the decorator something to wrap. The marker keeps a repeated call from wrapping twice.
        if (!services.IsAdded<DescribedRequirementRejectionMarker>())
        {
            services.AddSingleton(new DescribedRequirementRejectionMarker());
            services.TryAddTransient<IAuthorizationEvaluator, DefaultAuthorizationEvaluator>();
            services.TryDecorate<IAuthorizationEvaluator, DescribedRequirementRejectionEvaluator>();
        }

        // ASP.NET Core refuses an [Authorize] endpoint the authorization middleware never saw, but runs a
        // [RequiresFeature] endpoint unchecked; this guard closes that gap for every requirement-data endpoint.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<MatcherPolicy, AuthorizationRequirementDataGuardMatcherPolicy>()
        );

        return services;
    }

    private sealed class DescribedRequirementRejectionMarker;

    /// <summary>
    /// Adds the status-codes rewriter middleware to the ASP.NET Core request pipeline.
    /// It intercepts bare 401, 403, and 404 responses (without an existing body) and rewrites them
    /// as structured <c>application/problem+json</c> responses via <see cref="IProblemDetailsCreator"/>.
    /// A request carrying an <see cref="IStatusCodeRejectionFeature"/> is offered to that feature first,
    /// so the handler that failed the request (for example the tenant requirement's
    /// <c>g:tenant_required</c> rejection) writes its own response.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <remarks>
    /// <para>
    /// Call this before <c>UseAuthorization()</c> so it wraps authorization: a failed evaluation
    /// short-circuits, so a rewriter added after authorization never sees the rejection it should write.
    /// </para>
    /// <para>
    /// Notifies any registered <see cref="IStatusCodesRewriterCalledNotifier"/> (e.g.,
    /// <c>HeadlessServiceDefaultsStartupValidator</c>) synchronously before adding the middleware,
    /// and records the placement on an already-configured tenant catalog seam —
    /// <see cref="TenantCatalogPosture.StatusCodesRewriterRuntimeMarker"/> when authorization has not been
    /// added yet, <see cref="TenantCatalogPosture.StatusCodesRewriterAfterAuthorizationRuntimeMarker"/> when
    /// it has — so <c>TenantCatalogPostureValidator</c> can fail a catalog-resolution host whose mismatch
    /// rejection would otherwise stay distinguishable from the unknown-tenant rejection. Authorization that
    /// <c>WebApplication</c> adds implicitly, or that runs inside a <c>UseWhen</c> branch, is not visible
    /// here.
    /// </para>
    /// </remarks>
    /// <returns>The same application builder.</returns>
    public static IApplicationBuilder UseStatusCodesRewriter(this IApplicationBuilder app)
    {
        // Notify any registered observer (e.g. HeadlessServiceDefaultsStartupValidator) that the middleware was wired.
        if (
            app.ApplicationServices.GetService(typeof(IStatusCodesRewriterCalledNotifier))
            is IStatusCodesRewriterCalledNotifier notifier
        )
        {
            notifier.OnCalled();
        }

        // The notifier above is implemented by Headless.Api.ServiceDefaults, which a plain Headless.Api
        // catalog host does not reference; the posture manifest is the channel Headless.MultiTenancy's startup
        // validator can actually observe, and it is the same one UseHeadlessTenantCatalogResolution() writes
        // its pipeline marker to. Gated on an already-recorded seam so a host with no catalog does not
        // materialize an empty Catalog seam in the manifest — AddHeadlessTenancy(...) always runs before the
        // pipeline is composed, so a configured catalog is recorded by this point.
        if (
            app.ApplicationServices.GetService<TenantPostureManifest>() is { } manifest
            && manifest.IsConfigured(TenantCatalogPosture.Seam)
        )
        {
            manifest.MarkRuntimeApplied(
                TenantCatalogPosture.Seam,
                app.Properties.ContainsKey(_AuthorizationMiddlewareSetKey)
                    ? TenantCatalogPosture.StatusCodesRewriterAfterAuthorizationRuntimeMarker
                    : TenantCatalogPosture.StatusCodesRewriterRuntimeMarker
            );
        }

        return app.UseMiddleware<StatusCodesRewriterMiddleware>();
    }

    /// <summary>
    /// Registers <c>TenantResolutionMiddleware</c> as a singleton in the DI container.
    /// Call <see cref="UseTenantResolution"/> (or <see cref="SetupApiTenancy.UseHeadlessTenancy"/>)
    /// after this to add it to the pipeline.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The same service collection.</returns>
    internal static IServiceCollection AddTenantResolution(this IServiceCollection services)
    {
        services.TryAddSingleton<TenantResolutionMiddleware>();
        return services;
    }

    /// <summary>
    /// Adds <c>TenantResolutionMiddleware</c> to the pipeline. It reads the configured tenant claim
    /// from the authenticated principal and sets <see cref="Headless.MultiTenancy.ICurrentTenant"/>
    /// for the duration of the request. Endpoints decorated with <see cref="SkipTenantResolutionAttribute"/>
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
    internal static IApplicationBuilder UseTenantResolution(this IApplicationBuilder application)
    {
        return application.UseMiddleware<TenantResolutionMiddleware>();
    }

    /// <summary>
    /// Registers <c>TenantCatalogResolutionMiddleware</c> as a singleton in the DI container, together
    /// with the services its rejection and integrity paths depend on.
    /// Call <see cref="UseTenantCatalogResolution"/> (or
    /// <see cref="SetupApiTenancy.UseHeadlessTenantCatalogResolution"/>) after this to add it to the
    /// pipeline.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The same service collection.</returns>
    /// <remarks>
    /// Also registers the post-authorization mapping-integrity handler
    /// (<c>TenantIdentifierIntegrityHandler</c>) and
    /// <see cref="Microsoft.AspNetCore.Http.IHttpContextAccessor"/>, so cross-tenant integrity
    /// enforcement is inseparable from the middleware — a host wiring this low-level pair directly would
    /// otherwise get identifier resolution and ambient tenant assignment with tier-2 enforcement
    /// silently absent. <see cref="IProblemDetailsCreator"/> and its dependencies are registered for the
    /// same reason: every rejection path resolves it from request services.
    /// </remarks>
    internal static IServiceCollection AddTenantCatalogResolution(this IServiceCollection services)
    {
        // Every rejection path of this middleware (unknown/disabled/invalid outcomes, the claim
        // mismatch, and TenantResolutionMiddleware's claim-vs-feature fast path) resolves
        // IProblemDetailsCreator from request services. That must not depend on the host also calling
        // AddHeadlessProblemDetails(), or a catalog host turns every rejection into a runtime 500 while
        // startup validation stays green. TryAdd keeps AddHeadlessProblemDetails() and consumer
        // replacements authoritative; ProblemDetailsCreator's own dependencies are registered the same
        // way so the write path is resolvable in a host that registers nothing else.
        services.TryAddSingleton<IProblemDetailsCreator, ProblemDetailsCreator>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IBuildInformationAccessor, BuildInformationAccessor>();

        // TenantIdentifierIntegrityHandler needs a fallback route to the request when authorization does
        // not pass the HttpContext as the resource (the AppContext switch
        // Microsoft.AspNetCore.Authorization.SuppressUseHttpContextAsAuthorizationResource passes the
        // Endpoint instead).
        services.AddHttpContextAccessor();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IAuthorizationHandler, TenantIdentifierIntegrityHandler>()
        );

        return services;
    }

    /// <summary>
    /// Adds <c>TenantCatalogResolutionMiddleware</c> to the pipeline. It consults registered
    /// <c>ITenantIdentifierSource</c>s in registration order and resolves the first found
    /// identifier through the tenant catalog, setting <see cref="Headless.MultiTenancy.ICurrentTenant"/>
    /// on a match or short-circuiting with a fail-closed ProblemDetails response. Endpoints decorated
    /// with <see cref="SkipTenantResolutionAttribute"/> are bypassed entirely.
    /// </summary>
    /// <param name="application">The application builder.</param>
    /// <returns>The same application builder.</returns>
    /// <remarks>
    /// Place this after <c>UseRouting()</c> and before <c>UseAuthentication()</c> — separate from
    /// <see cref="UseTenantResolution"/>'s post-authentication claim placement. A one-time
    /// process-level warning is emitted when the middleware observes a request with no resolved
    /// endpoint (likely ordering misconfiguration). Prefer
    /// <see cref="SetupApiTenancy.UseHeadlessTenantCatalogResolution"/> when catalog resolution was
    /// configured through the tenancy builder — it guards against double-registration, no-ops for
    /// accessor-only hosts, and records the posture runtime marker validated at startup.
    /// </remarks>
    internal static IApplicationBuilder UseTenantCatalogResolution(this IApplicationBuilder application)
    {
        return application.UseMiddleware<TenantCatalogResolutionMiddleware>();
    }
}

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
