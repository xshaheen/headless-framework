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
