// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Authorization;

namespace Headless.Api.MultiTenancy;

/// <summary>
/// ASP.NET Core authorization requirement that enforces an ambient resolved tenant.
/// Add this to <c>DefaultPolicy</c> or <c>FallbackPolicy</c> to gate all protected
/// endpoints on the presence of a tenant:
/// <code>
/// options.FallbackPolicy = new AuthorizationPolicyBuilder()
///     .RequireAuthenticatedUser()
///     .AddRequirements(new TenantRequirement())
///     .Build();
/// </code>
/// </summary>
/// <remarks>
/// Handled by <c>TenantRequirementHandler</c>, which checks <see cref="Headless.Abstractions.ICurrentTenant.Id"/>.
/// On failure it stashes <c>TenantContextRequiredFeature</c> on the request so
/// <c>StatusCodesRewriterMiddleware</c> can emit the structured <c>g:tenant_required</c> 403 body.
/// Named-policy placement (<c>options.AddPolicy("name", ...)</c>) is NOT detected by the startup
/// validator and does NOT satisfy the framework enforcement guarantee.
/// </remarks>
[PublicAPI]
public sealed class TenantRequirement : IAuthorizationRequirement
{
    /// <summary>
    /// Discriminator attached to <see cref="Microsoft.AspNetCore.Authorization.AuthorizationFailureReason.Message"/>
    /// when the handler fails. Used internally for diagnostics — consumers that need to detect tenant
    /// failures should inspect <see cref="Microsoft.AspNetCore.Authorization.AuthorizationFailure.FailedRequirements"/>
    /// for a <see cref="TenantRequirement"/> instance instead of matching this string.
    /// </summary>
    internal const string FailureReason = "TenantContextRequired";
}
