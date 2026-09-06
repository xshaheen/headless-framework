// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Abstractions;
using Headless.Api.MultiTenancy;
using Headless.Checks;
using Headless.Constants;
using Headless.MultiTenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
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
    /// <see cref="UseHeadlessTenancy"/>'s post-authentication claim placement (KTD2). This method does
    /// not call <c>UseRouting()</c>, <c>UseAuthentication()</c>, or <c>UseAuthorization()</c>.
    /// Repeated invocations are idempotent. Accessor-only hosts (a catalog store configured via
    /// <c>HeadlessTenancyBuilder.Catalog(...)</c> with no <c>ResolveFromCatalog(...)</c> call) are a
    /// no-op — resolution middleware never runs for them (R18). Marks the tenant posture runtime marker
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

        if (
            seam is null
            || !seam.Capabilities.Contains(TenantCatalogPosture.ResolutionCapability, StringComparer.Ordinal)
        )
        {
            // Not configured, or accessor-only (store configured, resolution never requested) — R18's
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
    /// Registers <c>TenantCatalogResolutionMiddleware</c> — which enforces R19 mapping integrity for
    /// every identifier-resolved request against the default authentication scheme, independent of
    /// endpoint metadata or authorization policy — and the R19 post-authorization mapping integrity
    /// handler (<c>TenantIdentifierIntegrityHandler</c>) that covers endpoint-scoped
    /// (non-default) authentication schemes, and records the
    /// <see cref="TenantCatalogPosture.ResolutionCapability"/> capability on the
    /// <see cref="TenantCatalogPosture.Seam"/> posture seam — independent of and installed regardless
    /// of whether <see cref="ResolveFromClaims"/> is also configured (KTD2). A tenant store must be
    /// configured separately via <c>HeadlessTenancyBuilder.Catalog(...)</c>; otherwise startup
    /// validation fails (R18). Call <see cref="SetupApiTenancy.UseHeadlessTenantCatalogResolution"/>
    /// after <c>UseRouting()</c> and before <c>UseAuthentication()</c> to wire the middleware into the
    /// pipeline. v1 ships no built-in <see cref="ITenantIdentifierSource"/> — register one through
    /// <paramref name="configure"/>, or resolution silently never activates for any request (R5).
    /// </remarks>
    public HeadlessHttpTenancyBuilder ResolveFromCatalog(
        Action<HeadlessTenantCatalogResolutionBuilder>? configure = null
    )
    {
        // Catalog-only hosts (no ResolveFromClaims()) still need ICurrentTenant/ICurrentTenantAccessor
        // for the middleware's ambient Change(), and IOptions<MultiTenancyOptions> for
        // TenantIdentifierIntegrityHandler's R19 claim-type read. Idempotent — a host that also calls
        // ResolveFromClaims(...) merely repeats the same TryAdd/AddOptions registrations.
        _builder.ApplicationBuilder.AddHeadlessMultiTenancy();

        // Registers the middleware together with the services its rejection and R19 integrity paths need
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

/// <summary>Registers <see cref="ITenantIdentifierSource"/>s consulted by pre-auth tenant catalog resolution.</summary>
/// <remarks>
/// Sources are resolved in registration order; <c>TenantCatalogResolutionMiddleware</c> uses the first
/// non-<see langword="null"/> identifier returned. v1 ships no built-in source — the deferred
/// host/route/header identifier strategies (a separate unit of work) implement this interface and call
/// <see cref="AddSource{TSource}"/> from their own setup extensions.
/// </remarks>
[PublicAPI]
public sealed class HeadlessTenantCatalogResolutionBuilder
{
    private readonly IServiceCollection _services;

    internal HeadlessTenantCatalogResolutionBuilder(IServiceCollection services)
    {
        _services = Argument.IsNotNull(services);
    }

    /// <summary>Registers an <see cref="ITenantIdentifierSource"/> implementation resolved from DI.</summary>
    /// <typeparam name="TSource">The identifier source implementation type.</typeparam>
    /// <returns>The same builder, to allow chaining.</returns>
    public HeadlessTenantCatalogResolutionBuilder AddSource<TSource>()
        where TSource : class, ITenantIdentifierSource
    {
        _services.AddSingleton<ITenantIdentifierSource, TSource>();
        return this;
    }

    /// <summary>Registers an already-constructed <see cref="ITenantIdentifierSource"/> instance.</summary>
    /// <param name="source">The identifier source instance.</param>
    /// <returns>The same builder, to allow chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    public HeadlessTenantCatalogResolutionBuilder AddSource(ITenantIdentifierSource source)
    {
        Argument.IsNotNull(source);
        _services.AddSingleton(source);
        return this;
    }
}

/// <summary>Records that Headless authorization should require a resolved tenant.</summary>
[PublicAPI]
public sealed class HeadlessAuthorizationTenancyBuilder
{
    /// <summary>The seam name reported in the tenant posture manifest.</summary>
    public const string Seam = "Authorization";

    /// <summary>Capability label reported by <see cref="RequireTenant"/>.</summary>
    public const string RequireTenantCapability = "require-tenant";

    /// <summary>Diagnostic code emitted when authorization tenancy is configured without a tenant policy.</summary>
    public const string AuthorizationPolicyMissingDiagnosticCode = "HEADLESS_TENANCY_AUTHORIZATION_POLICY_MISSING";

    private readonly HeadlessTenancyBuilder _builder;

    internal HeadlessAuthorizationTenancyBuilder(HeadlessTenancyBuilder builder)
    {
        _builder = Argument.IsNotNull(builder);
    }

    /// <summary>Requires an ambient tenant through ASP.NET Core authorization.</summary>
    /// <returns>The same authorization tenancy builder.</returns>
    /// <remarks>
    /// The structured <c>g:tenant_required</c> 403 body is written by
    /// <c>StatusCodesRewriterMiddleware</c> after authorization rejects the request. The middleware
    /// is wired in by <see cref="SetupApiServices.AddHeadlessProblemDetails"/> /
    /// <c>Headless.Api.ServiceDefaults</c>; consumers that do not use ServiceDefaults must register
    /// it via <see cref="SetupMiddlewares.UseStatusCodesRewriter"/> to receive the discriminator.
    ///
    /// <para>
    /// <c>TenantRequirement</c> in named policies (<c>options.AddPolicy("name", ...)</c>) is NOT
    /// detected by the startup validator and does NOT satisfy the framework's enforcement
    /// guarantee. Place <c>TenantRequirement</c> in <c>DefaultPolicy</c> or <c>FallbackPolicy</c>
    /// for framework-level enforcement.
    /// </para>
    /// </remarks>
    public HeadlessAuthorizationTenancyBuilder RequireTenant()
    {
        _builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IAuthorizationHandler, TenantRequirementHandler>()
        );
        _builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHeadlessTenancyValidator, HeadlessAuthorizationTenancyValidator>()
        );

        _builder.RecordSeam(Seam, TenantPostureStatus.Enforcing, RequireTenantCapability);

        return this;
    }
}

internal sealed class HeadlessAuthorizationTenancyValidator : IHeadlessTenancyValidator
{
    public IEnumerable<HeadlessTenancyDiagnostic> Validate(HeadlessTenancyValidationContext context)
    {
        Argument.IsNotNull(context);

        if (!context.Manifest.IsConfigured(HeadlessAuthorizationTenancyBuilder.Seam))
        {
            yield break;
        }

        var options = context.Services.GetService<IOptions<AuthorizationOptions>>()?.Value;

        if (
            options is null
            || (
                !_ContainsTenantRequirement(options.DefaultPolicy)
                && !_ContainsTenantRequirement(options.FallbackPolicy)
            )
        )
        {
            yield return HeadlessTenancyDiagnostic.Error(
                HeadlessAuthorizationTenancyBuilder.Seam,
                HeadlessAuthorizationTenancyBuilder.AuthorizationPolicyMissingDiagnosticCode,
                "Authorization tenant enforcement is configured, but neither DefaultPolicy nor FallbackPolicy "
                    + "includes TenantRequirement. Add it via "
                    + "AddAuthorization(o => o.FallbackPolicy = new AuthorizationPolicyBuilder()"
                    + ".RequireAuthenticatedUser().AddRequirements(new TenantRequirement()).Build()). "
                    + "TenantRequirement in named policies (options.AddPolicy(\"name\", ...)) is NOT "
                    + "detected by this validator and does NOT satisfy the enforcement guarantee — only "
                    + "DefaultPolicy / FallbackPolicy are inspected. Named-policy enforcement is the "
                    + "consumer's responsibility."
            );
        }
    }

    private static bool _ContainsTenantRequirement(AuthorizationPolicy? policy)
    {
        return policy?.Requirements.OfType<TenantRequirement>().Any() == true;
    }
}

internal sealed class HeadlessHttpTenancyValidator : IHeadlessTenancyValidator
{
    public IEnumerable<HeadlessTenancyDiagnostic> Validate(HeadlessTenancyValidationContext context)
    {
        Argument.IsNotNull(context);

        if (
            context.Manifest.IsConfigured(HeadlessHttpTenancyBuilder.Seam)
            && !context.Manifest.HasRuntimeMarker(
                HeadlessHttpTenancyBuilder.Seam,
                HeadlessHttpTenancyBuilder.UseHeadlessTenancyMarker
            )
        )
        {
            yield return HeadlessTenancyDiagnostic.Error(
                HeadlessHttpTenancyBuilder.Seam,
                HeadlessHttpTenancyBuilder.HttpMiddlewareMissingDiagnosticCode,
                "HTTP tenant resolution is configured, but UseHeadlessTenancy() was not applied."
            );
        }
    }
}

/// <summary>Options for HTTP tenant resolution.</summary>
[PublicAPI]
public sealed class MultiTenancyOptions
{
    /// <summary>Claim type to read tenant ID from. Defaults to <c>tenant_id</c>.</summary>
    public string ClaimType { get; set; } = UserClaimTypes.TenantId;
}

internal sealed class MultiTenancyOptionsValidator : AbstractValidator<MultiTenancyOptions>
{
    public MultiTenancyOptionsValidator()
    {
        RuleFor(x => x.ClaimType).NotEmpty();
    }
}
