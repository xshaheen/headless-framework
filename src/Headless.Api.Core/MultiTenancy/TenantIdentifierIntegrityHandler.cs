// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
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
/// <para>
/// Scope boundary: the request-wide rejection marker is set only for the principal the authorization
/// pipeline itself authenticated for this request. Reference identity against
/// <see cref="HttpContext.User"/> is the proxy used for exactly that — every framework path that
/// authenticates on the request's behalf (<c>PolicyEvaluator</c> for default and endpoint-scoped
/// schemes, MVC's <c>AuthorizeFilter</c>, SignalR, Blazor SSR, and <c>IClaimsTransformation</c> output)
/// assigns its principal to <see cref="HttpContext.User"/> and then passes that same reference into the
/// evaluation. A principal an endpoint materializes itself — calling
/// <c>HttpContext.AuthenticateAsync("SomeScheme")</c> and handing the resulting
/// principal to <c>IAuthorizationService.AuthorizeAsync</c> — is deliberately outside that boundary,
/// even though it is a credential the request genuinely presented: its evaluation fails on its own
/// terms via <c>context.Fail(...)</c>, but it does
/// not stamp the marker and so does not rewrite the response. Widening the boundary to cover
/// self-materialized principals would re-open the side-channel hole this gate closes, where an
/// authorization check an endpoint makes against somebody else's principal replaces the caller's own
/// successful response with a tenant-resolution rejection.
/// </para>
/// </remarks>
internal sealed class TenantIdentifierIntegrityHandler(
    IOptions<MultiTenancyOptions> options,
    IHttpContextAccessor httpContextAccessor
) : IAuthorizationHandler
{
    private const string _MismatchFailureReason =
        "Headless tenant identifier does not match the authenticated tenant claim.";

    public Task HandleAsync(AuthorizationHandlerContext context)
    {
        Argument.IsNotNull(context);

        // AuthorizationMiddleware passes the HttpContext as the authorization resource by default, but
        // the Microsoft.AspNetCore.Authorization.SuppressUseHttpContextAsAuthorizationResource AppContext
        // switch makes it pass the Endpoint instead. Falling back to the accessor keeps R19 enforced
        // under either resource shape; only a genuinely context-free evaluation falls through.
        var httpContext = context.Resource as HttpContext ?? httpContextAccessor.HttpContext;

        if (httpContext is null)
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

        // The authorization context's principal, not HttpContext.User: for endpoint-scoped schemes
        // PolicyEvaluator authenticates into this principal, and it stays correct when the resource is
        // an Endpoint rather than the HttpContext.
        if (!TenantIdentifierIntegrityChecker.IsMismatch(context.User, resolvedFeature.TenantId, options.Value))
        {
            return Task.CompletedTask;
        }

        // The failure is always correct — this evaluation genuinely mismatches — but the request-wide
        // response override is not. This handler runs for EVERY IAuthorizationService.AuthorizeAsync call
        // in the process, including side-channel checks an endpoint makes against somebody else's
        // principal (permission previews, admin tooling, delegated access). A foreign principal's tenant
        // must not poison the request-wide marker, or StatusCodesRewriterMiddleware would replace the
        // caller's own successful response with a tenant-resolution rejection. Reference identity is a
        // deliberate scope boundary, not just a foreign-principal filter — see the type remarks for what
        // it excludes and why that exclusion is intended.
        if (ReferenceEquals(context.User, httpContext.User))
        {
            httpContext.Features.Set(TenantIdentifierMismatchFeature.Instance);
        }

        context.Fail(new AuthorizationFailureReason(this, _MismatchFailureReason));

        return Task.CompletedTask;
    }
}
