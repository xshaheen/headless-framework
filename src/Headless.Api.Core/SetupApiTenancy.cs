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

        var manifest = application.ApplicationServices.GetService<TenantPostureManifest>();

        if (manifest is null)
        {
            throw new InvalidOperationException(
                "UseHeadlessTenancy() requires AddHeadlessTenancy(...). Configure HTTP tenancy with "
                    + "builder.AddHeadlessTenancy(tenancy => tenancy.Http(http => http.ResolveFromClaims()))."
            );
        }

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
    /// Registers <see cref="Headless.Abstractions.ICurrentTenant"/>, <c>TenantResolutionMiddleware</c>,
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
