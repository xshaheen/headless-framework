// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Abstractions;
using Headless.Api.Abstractions;
using Headless.Api.MultiTenancy;
using Headless.Checks;
using Headless.Constants;
using Headless.MultiTenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Headless.Api;

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
    /// </remarks>
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
    /// <param name="configure">Optional tenant resolution options.</param>
    /// <returns>The same HTTP tenancy builder.</returns>
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

    /// <summary>
    /// Diagnostic code emitted when a consumer registers an
    /// <see cref="IAuthorizationMiddlewareResultHandler"/> after <see cref="RequireTenant"/>,
    /// replacing the framework's tenant-mapping handler.
    /// </summary>
    public const string AuthorizationResultHandlerReplacedDiagnosticCode =
        "HEADLESS_TENANCY_AUTHORIZATION_RESULT_HANDLER_REPLACED";

    private readonly HeadlessTenancyBuilder _builder;

    internal HeadlessAuthorizationTenancyBuilder(HeadlessTenancyBuilder builder)
    {
        _builder = Argument.IsNotNull(builder);
    }

    /// <summary>Requires an ambient tenant through ASP.NET Core authorization.</summary>
    /// <returns>The same authorization tenancy builder.</returns>
    /// <remarks>
    /// Call <c>RequireTenant()</c> AFTER any custom <see cref="IAuthorizationMiddlewareResultHandler"/>
    /// registrations. ASP.NET Core resolves the last-registered handler at runtime; consumer
    /// registrations made after <c>RequireTenant()</c> replace the framework's tenant-mapping
    /// handler and silently disable the structured <c>g:tenant_required</c> 403 response. The
    /// startup validator emits <see cref="AuthorizationResultHandlerReplacedDiagnosticCode"/> when
    /// it detects this misordering.
    ///
    /// <para>
    /// <c>TenantRequirement</c> in named policies
    /// (<c>options.AddPolicy("name", ...)</c>) is NOT detected by the startup validator and does
    /// NOT satisfy the framework's enforcement guarantee. Place <c>TenantRequirement</c> in
    /// <c>DefaultPolicy</c> or <c>FallbackPolicy</c> for framework-level enforcement.
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
        _RegisterAuthorizationResultHandler(_builder.Services);

        _builder.RecordSeam(Seam, TenantPostureStatus.Enforcing, RequireTenantCapability);

        return this;
    }

    private static void _RegisterAuthorizationResultHandler(IServiceCollection services)
    {
        if (
            services.Any(descriptor =>
                descriptor.ServiceType == typeof(TenantAuthorizationMiddlewareResultHandlerRegistration)
            )
        )
        {
            return;
        }

        services.TryAddTransient<IAuthorizationMiddlewareResultHandler, AuthorizationMiddlewareResultHandler>();
        services.Decorate<IAuthorizationMiddlewareResultHandler, TenantAuthorizationMiddlewareResultHandler>();
        services.AddSingleton<TenantAuthorizationMiddlewareResultHandlerRegistration>();
    }
}

internal sealed class TenantAuthorizationMiddlewareResultHandlerRegistration;

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

        // Detect when a consumer registered a custom IAuthorizationMiddlewareResultHandler AFTER
        // RequireTenant(). ASP.NET Core resolves the last-registered handler, so later registrations
        // silently replace the framework's tenant-mapping handler. Only check when RequireTenant()
        // actually ran (proven by the registration marker); otherwise the framework's handler was
        // never registered and a foreign handler is not a misordering.
        var requireTenantInvoked =
            context.Services.GetService<TenantAuthorizationMiddlewareResultHandlerRegistration>() is not null;

        if (requireTenantInvoked)
        {
            var resolved = context.Services.GetService<IAuthorizationMiddlewareResultHandler>();

            if (resolved is not null and not TenantAuthorizationMiddlewareResultHandler)
            {
                yield return HeadlessTenancyDiagnostic.Error(
                    HeadlessAuthorizationTenancyBuilder.Seam,
                    HeadlessAuthorizationTenancyBuilder.AuthorizationResultHandlerReplacedDiagnosticCode,
                    "Authorization tenant enforcement is configured, but the resolved "
                        + $"IAuthorizationMiddlewareResultHandler is '{resolved.GetType().FullName}' — not the "
                        + "framework's TenantAuthorizationMiddlewareResultHandler. The tenant-mapping handler "
                        + "has been replaced, so tenant authorization failures will not produce the structured "
                        + "g:tenant_required 403 response. Call .Authorization(a => a.RequireTenant()) AFTER "
                        + "all custom IAuthorizationMiddlewareResultHandler registrations."
                );
            }
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
