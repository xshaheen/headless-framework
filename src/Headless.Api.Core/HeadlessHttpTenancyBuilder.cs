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

/// <summary>Records that Headless HTTP tenancy should resolve tenants from authenticated user claims.</summary>
[PublicAPI]
public sealed class HeadlessHttpTenancyBuilder
{
    /// <summary>The seam name reported in the tenant posture manifest.</summary>
    public const string Seam = "Http";

    /// <summary>Capability label reported by <see cref="ResolveFromClaims"/>.</summary>
    public const string ResolveFromClaimsCapability = "resolve-from-claims";

    /// <summary>Runtime marker recorded when <c>UseHeadlessTenancy()</c> is invoked.</summary>
    public const string UseHeadlessTenancyMarker = "UseHeadlessTenancy";

    /// <summary>Diagnostic code emitted when HTTP tenancy is configured but <c>UseHeadlessTenancy()</c> was not invoked.</summary>
    public const string HttpMiddlewareMissingDiagnosticCode = "HEADLESS_TENANCY_HTTP_MIDDLEWARE_MISSING";

    private readonly HeadlessTenancyBuilder _builder;

    internal HeadlessHttpTenancyBuilder(HeadlessTenancyBuilder builder)
    {
        _builder = Argument.IsNotNull(builder);
    }

    /// <summary>Configures HTTP tenant resolution from authenticated principal claims.</summary>
    /// <param name="configure">Optional callback to configure <see cref="MultiTenancyOptions"/>.</param>
    /// <returns>The same HTTP tenancy builder.</returns>
    /// <remarks>
    /// Registers <see cref="Headless.MultiTenancy.ICurrentTenant"/>, <c>TenantResolutionMiddleware</c>,
    /// and <c>HeadlessHttpTenancyValidator</c>. Records the <c>Http</c> seam in the tenant posture
    /// manifest so startup validation can detect whether <see cref="SetupApiTenancy.UseHeadlessTenancy"/>
    /// was subsequently called.
    /// </remarks>
    public HeadlessHttpTenancyBuilder ResolveFromClaims(Action<MultiTenancyOptions>? configure = null)
    {
        _builder.ApplicationBuilder.AddHeadlessMultiTenancy(configure);

        _builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHeadlessTenancyValidator, HeadlessHttpTenancyValidator>()
        );

        _builder.RecordSeam(Seam, TenantPostureStatus.Configured, ResolveFromClaimsCapability);

        return this;
    }

    /// <summary>Configures pre-auth tenant catalog identifier resolution.</summary>
    /// <param name="configure">Optional callback to register <see cref="ITenantIdentifierSource"/>s.</param>
    /// <returns>The same HTTP tenancy builder.</returns>
    /// <remarks>
    /// Registers <c>TenantCatalogResolutionMiddleware</c> — which enforces mapping integrity for
    /// every identifier-resolved request against the default authentication scheme, independent of
    /// endpoint metadata or authorization policy — and the post-authorization mapping integrity
    /// handler (<c>TenantIdentifierIntegrityHandler</c>) that covers endpoint-scoped
    /// (non-default) authentication schemes, and records the
    /// <see cref="TenantCatalogPosture.ResolutionCapability"/> capability on the
    /// <see cref="TenantCatalogPosture.Seam"/> posture seam — independent of and installed regardless
    /// of whether <see cref="ResolveFromClaims"/> is also configured. A tenant store must be
    /// configured separately via <c>HeadlessTenancyBuilder.Catalog(...)</c>; otherwise startup
    /// validation fails. Call <see cref="SetupApiTenancy.UseHeadlessTenantCatalogResolution"/>
    /// after <c>UseRouting()</c> and before <c>UseAuthentication()</c> to wire the middleware into the
    /// pipeline. With zero registered sources resolution silently never activates for any request —
    /// register one through <paramref name="configure"/>.
    /// </remarks>
    public HeadlessHttpTenancyBuilder ResolveFromCatalog(
        Action<HeadlessTenantCatalogResolutionBuilder>? configure = null
    )
    {
        // Catalog-only hosts (no ResolveFromClaims()) still need ICurrentTenant/ICurrentTenantAccessor
        // for the middleware's ambient Change(), and IOptions<MultiTenancyOptions> for
        // TenantIdentifierIntegrityHandler's claim-type read. Idempotent — a host that also calls
        // ResolveFromClaims(...) merely repeats the same TryAdd/AddOptions registrations.
        _builder.ApplicationBuilder.AddHeadlessMultiTenancy();

        // Registers the middleware together with the services its rejection and mapping-integrity paths need
        // (IProblemDetailsCreator and its dependencies, IHttpContextAccessor, and
        // TenantIdentifierIntegrityHandler) — they travel with the feature so the low-level
        // AddTenantCatalogResolution()/UseTenantCatalogResolution() pair is equally protected.
        _builder.Services.AddTenantCatalogResolution();

        var sourcesBuilder = new HeadlessTenantCatalogResolutionBuilder(_builder.Services);
        configure?.Invoke(sourcesBuilder);

        _builder.RecordSeam(
            TenantCatalogPosture.Seam,
            TenantPostureStatus.Enforcing,
            TenantCatalogPosture.ResolutionCapability
        );

        return this;
    }
}
