// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Headless.Checks;
using Headless.Constants;
using Headless.MultiTenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Headless.Api.MultiTenancy;

/// <summary>
/// Authoritative R19 mapping-integrity check: whenever identifier-based catalog resolution resolved a
/// canonical tenant id for the request (<see cref="TenantIdentifierResolvedFeature"/>) and the
/// authenticated principal also carries a tenant claim, the two must canonicalize to the same tenant.
/// </summary>
/// <remarks>
/// <para>
/// Implements the raw <see cref="IAuthorizationHandler.HandleAsync(AuthorizationHandlerContext)"/>
/// overload directly (not <see cref="AuthorizationHandler{TRequirement}"/>) so it runs for every
/// authorization evaluation regardless of which requirements a policy declares — installing this
/// handler is independent of <c>ResolveFromClaims</c> / <c>RequireTenant()</c> policy wiring (KTD2).
/// </para>
/// <para>
/// Runs inside <c>AuthorizationMiddleware</c>, strictly after <c>PolicyEvaluator</c> has authenticated
/// the policy's declared scheme(s) and assigned the result to <c>HttpContext.User</c> — this is what
/// makes the check correct for endpoint-scoped or non-default authentication schemes, where the
/// principal is not materialized until authorization evaluation time. A plain post-<c>UseAuthorization()</c>
/// middleware would not observe a failing case at all, since the authorization pipeline short-circuits
/// before <c>next()</c> on failure.
/// </para>
/// <para>
/// Compares the request feature against the claim directly — never against the ambient
/// <see cref="ICurrentTenant.Id"/>, which claim resolution's own <c>Change()</c> may have already
/// overwritten by the time this handler runs.
/// </para>
/// </remarks>
internal sealed class TenantIdentifierIntegrityHandler(IOptions<MultiTenancyOptions> options) : IAuthorizationHandler
{
    private const string _MismatchFailureReason =
        "Headless tenant identifier does not match the authenticated tenant claim.";

    public Task HandleAsync(AuthorizationHandlerContext context)
    {
        Argument.IsNotNull(context);

        if (context.Resource is not HttpContext httpContext)
        {
            return Task.CompletedTask;
        }

        var resolvedFeature = httpContext.Features.Get<TenantIdentifierResolvedFeature>();

        if (resolvedFeature is null)
        {
            // No identifier resolution happened for this request — claim-only requests keep today's
            // store-free path untouched (R8).
            return Task.CompletedTask;
        }

        var claimTenantId = _GetClaimTenantId(httpContext.User, options.Value);

        if (claimTenantId is null || string.Equals(claimTenantId, resolvedFeature.TenantId, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        httpContext.Features.Set(TenantIdentifierMismatchFeature.Instance);
        context.Fail(new AuthorizationFailureReason(this, _MismatchFailureReason));

        return Task.CompletedTask;
    }

    private static string? _GetClaimTenantId(ClaimsPrincipal principal, MultiTenancyOptions options)
    {
        var claimType = string.IsNullOrWhiteSpace(options.ClaimType) ? UserClaimTypes.TenantId : options.ClaimType;

        var value = string.Equals(claimType, UserClaimTypes.TenantId, StringComparison.Ordinal)
            ? principal.GetTenantId()
            : principal.FindFirst(claimType)?.Value;

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
