// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Abstractions;
using Headless.Api.MultiTenancy;
using Headless.Checks;
using Headless.Constants;
using Headless.MultiTenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Headless.Api;

/// <summary>
/// Extension methods and builder types for configuring Headless multi-tenancy on the HTTP pipeline.
/// </summary>
[PublicAPI]
public static class SetupApiTenancy
{
    /// <summary>
    /// Enables the framework multi-tenancy primitives and configures how HTTP tenant resolution should read tenant claims.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="configure">Optional tenant resolution configuration.</param>
    /// <returns>The same host application builder.</returns>
    internal static IHostApplicationBuilder AddHeadlessMultiTenancy(
        this IHostApplicationBuilder builder,
        Action<MultiTenancyOptions>? configure = null
    )
    {
        Argument.IsNotNull(builder);

        var optionsBuilder = builder.Services.AddOptions<MultiTenancyOptions, MultiTenancyOptionsValidator>();

        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        optionsBuilder.PostConfigure(options =>
        {
            if (string.IsNullOrWhiteSpace(options.ClaimType))
            {
                options.ClaimType = UserClaimTypes.TenantId;
            }
        });

        builder.Services.TryAddSingleton<ICurrentTenantAccessor>(AsyncLocalCurrentTenantAccessor.Instance);
        // Removes NullCurrentTenant fallback; preserves consumer-supplied ICurrentTenant.
        builder.Services.AddOrReplaceFallbackSingleton<ICurrentTenant, NullCurrentTenant, CurrentTenant>();

        return builder;
    }

    /// <summary>Configures HTTP tenant resolution through the root Headless tenancy builder.</summary>
    /// <param name="builder">The root tenancy builder.</param>
    /// <param name="configure">The HTTP tenancy configuration callback.</param>
    /// <returns>The same root tenancy builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    public static HeadlessTenancyBuilder Http(
        this HeadlessTenancyBuilder builder,
        Action<HeadlessHttpTenancyBuilder> configure
    )
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNull(configure);

        configure(new HeadlessHttpTenancyBuilder(builder));

        return builder;
    }

    /// <summary>Configures HTTP authorization tenancy through the root Headless tenancy builder.</summary>
    /// <param name="builder">The root tenancy builder.</param>
    /// <param name="configure">The authorization tenancy configuration callback.</param>
    /// <returns>The same root tenancy builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="configure"/> is <see langword="null"/>.</exception>
    public static HeadlessTenancyBuilder Authorization(
        this HeadlessTenancyBuilder builder,
        Action<HeadlessAuthorizationTenancyBuilder> configure
    )
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNull(configure);

        configure(new HeadlessAuthorizationTenancyBuilder(builder));

        return builder;
    }

    /// <summary>Applies Headless HTTP tenant resolution when HTTP tenancy was configured.</summary>
    /// <param name="application">The application builder.</param>
    /// <returns>The same application builder.</returns>
    /// <remarks>
    /// Register this after <c>UseAuthentication()</c> and before <c>UseAuthorization()</c>.
    /// This method does not call either authentication or authorization middleware.
    /// Repeated invocations are idempotent — <c>TenantResolutionMiddleware</c> is added at most once.
    /// When HTTP tenancy has not been configured (i.e., <c>ResolveFromClaims()</c> was not called),
    /// this method is a no-op and returns the application builder unchanged.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="application"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// <c>AddHeadlessTenancy()</c> was not called before <c>UseHeadlessTenancy()</c>. The
    /// <c>TenantPostureManifest</c> service is not registered.
    /// </exception>
    public static IApplicationBuilder UseHeadlessTenancy(this IApplicationBuilder application)
    {
        Argument.IsNotNull(application);

        var manifest =
            application.ApplicationServices.GetService<TenantPostureManifest>()
            ?? throw new InvalidOperationException(
                "UseHeadlessTenancy() requires AddHeadlessTenancy(...). Configure HTTP tenancy with "
                    + "builder.AddHeadlessTenancy(tenancy => tenancy.Http(http => http.ResolveFromClaims()))."
            );

        if (!manifest.IsConfigured(HeadlessHttpTenancyBuilder.Seam))
        {
            return application;
        }

        // Short-circuit on repeat invocations so consumer mistakes (double-registering the middleware)
        // do not stack TenantResolutionMiddleware in the pipeline.
        if (
            manifest.HasRuntimeMarker(
                HeadlessHttpTenancyBuilder.Seam,
                HeadlessHttpTenancyBuilder.UseHeadlessTenancyMarker
            )
        )
        {
            return application;
        }

        manifest.MarkRuntimeApplied(
            HeadlessHttpTenancyBuilder.Seam,
            HeadlessHttpTenancyBuilder.UseHeadlessTenancyMarker
        );

        return application.UseTenantResolution();
    }

    /// <summary>
    /// Applies Headless pre-auth tenant catalog identifier resolution when
    /// <c>HeadlessHttpTenancyBuilder.ResolveFromCatalog</c> was configured.
    /// </summary>
    /// <param name="application">The application builder.</param>
    /// <returns>The same application builder.</returns>
    /// <remarks>
    /// Register this after <c>UseRouting()</c> and before <c>UseAuthentication()</c> — separate from
    /// <see cref="UseHeadlessTenancy"/>'s post-authentication claim placement. This method does
    /// not call <c>UseRouting()</c>, <c>UseAuthentication()</c>, or <c>UseAuthorization()</c>.
    /// Repeated invocations are idempotent. Accessor-only hosts (a catalog store configured via
    /// <c>HeadlessTenancyBuilder.Catalog(...)</c> with no <c>ResolveFromCatalog(...)</c> call) are a
    /// no-op — resolution middleware never runs for them. Marks the tenant posture runtime marker
    /// the startup validator checks (<c>TenantCatalogPosture.ResolutionPipelineRuntimeMarker</c>) only
    /// when at least one <see cref="ITenantIdentifierSource"/> was registered — a resolution-capable
    /// seam with this hook wired but zero sources would otherwise never actually resolve anything.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="application"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// <c>AddHeadlessTenancy()</c> was not called before <c>UseHeadlessTenantCatalogResolution()</c>. The
    /// <c>TenantPostureManifest</c> service is not registered.
    /// </exception>
    public static IApplicationBuilder UseHeadlessTenantCatalogResolution(this IApplicationBuilder application)
    {
        Argument.IsNotNull(application);

        var manifest =
            application.ApplicationServices.GetService<TenantPostureManifest>()
            ?? throw new InvalidOperationException(
                "UseHeadlessTenantCatalogResolution() requires AddHeadlessTenancy(...). Configure catalog "
                    + "resolution with builder.AddHeadlessTenancy(tenancy => tenancy"
                    + ".Catalog(catalog => catalog.UseInMemory(...)).Http(http => http.ResolveFromCatalog(...)))."
            );

        var seam = manifest.GetSeam(TenantCatalogPosture.Seam);

        if (seam?.Capabilities.Contains(TenantCatalogPosture.ResolutionCapability, StringComparer.Ordinal) != true)
        {
            // Not configured, or accessor-only (store configured, resolution never requested) — this is the
            // explicit accessor-only carve-out. The resolution middleware must never run for these hosts.
            return application;
        }

        // Short-circuit on repeat invocations so consumer mistakes (double-registering the middleware)
        // do not stack TenantCatalogResolutionMiddleware in the pipeline.
        if (manifest.HasRuntimeMarker(TenantCatalogPosture.Seam, _UseTenantCatalogResolutionMarker))
        {
            return application;
        }

        manifest.MarkRuntimeApplied(TenantCatalogPosture.Seam, _UseTenantCatalogResolutionMarker);

        if (application.ApplicationServices.GetServices<ITenantIdentifierSource>().Any())
        {
            manifest.MarkRuntimeApplied(
                TenantCatalogPosture.Seam,
                TenantCatalogPosture.ResolutionPipelineRuntimeMarker
            );
        }

        return application.UseTenantCatalogResolution();
    }

    // Idempotency marker for UseHeadlessTenantCatalogResolution(), distinct from
    // TenantCatalogPosture.ResolutionPipelineRuntimeMarker: the latter is conditional on at least one
    // identifier source being registered, while double-registration must be guarded regardless of
    // source count.
    private const string _UseTenantCatalogResolutionMarker = "UseTenantCatalogResolution";
}
