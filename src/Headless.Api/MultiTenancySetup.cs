// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Abstractions;
using Headless.Api.Middlewares;
using Headless.Checks;
using Headless.Constants;
using Headless.MultiTenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Headless.Api;

[PublicAPI]
public static class MultiTenancySetup
{
    /// <summary>
    /// Enables the framework multi-tenancy primitives and configures how HTTP tenant resolution should read tenant claims.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="configure">Optional tenant resolution configuration.</param>
    /// <returns>The same host application builder.</returns>
    public static IHostApplicationBuilder AddHeadlessMultiTenancy(
        this IHostApplicationBuilder builder,
        Action<MultiTenancyOptions>? configure = null
    )
    {
        var optionsBuilder = builder.Services.AddOptions<MultiTenancyOptions, MultiTenancyOptionsValidator>();

        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

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

        manifest.MarkRuntimeApplied(HeadlessHttpTenancyBuilder.Seam, HeadlessHttpTenancyBuilder.UseHeadlessTenancyMarker);

        return application.UseTenantResolution();
    }
}

/// <summary>Records that Headless HTTP tenancy should resolve tenants from authenticated user claims.</summary>
public sealed class HeadlessHttpTenancyBuilder
{
    public const string Seam = "Http";
    public const string ResolveFromClaimsCapability = "resolve-from-claims";
    public const string UseHeadlessTenancyMarker = "UseHeadlessTenancy";

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

        _builder.RecordSeam(Seam, TenantPostureStatuses.Configured, ResolveFromClaimsCapability);

        return this;
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
                "HEADLESS_TENANCY_HTTP_MIDDLEWARE_MISSING",
                "HTTP tenant resolution is configured, but UseHeadlessTenancy() was not applied."
            );
        }
    }
}

/// <summary>Options for HTTP tenant resolution.</summary>
public sealed class MultiTenancyOptions
{
    /// <summary>Claim type to read tenant ID from. Defaults to <c>tenant_id</c>.</summary>
    public string ClaimType { get; set; } = UserClaimTypes.TenantId;
}

internal sealed class MultiTenancyOptionsValidator : AbstractValidator<MultiTenancyOptions>
{
    public MultiTenancyOptionsValidator()
    {
        RuleFor(x => x.ClaimType).NotNull();
    }
}
