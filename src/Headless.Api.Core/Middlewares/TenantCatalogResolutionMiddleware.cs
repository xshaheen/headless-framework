// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Api.MultiTenancy;
using Headless.Checks;
using Headless.MultiTenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Api.Middlewares;

/// <summary>
/// Pre-auth tenant catalog identifier resolution. Consults registered <see cref="ITenantIdentifierSource"/>s
/// in registration order (first non-<see langword="null"/> identifier wins), resolves through
/// <see cref="ITenantCatalogService"/>, and either sets the ambient tenant and continues, or short-circuits
/// with a fail-closed <c>ProblemDetails</c> response before the endpoint executes (R6, R11).
/// </summary>
/// <remarks>
/// Registered through its own pipeline hook — <c>SetupApiTenancy.UseHeadlessTenantCatalogResolution</c> —
/// separate from the existing post-auth claim hook (<c>UseHeadlessTenancy</c>). Documented ordering
/// contract: after <c>UseRouting()</c> (so <see cref="SkipTenantResolutionAttribute"/> endpoint metadata
/// is resolvable) and before <c>UseAuthentication()</c> (KTD2). With zero registered sources, or when
/// every source returns <see langword="null"/>, this middleware no-ops and the request continues as host
/// context (R5). Store or cache infrastructure faults from <see cref="ITenantCatalogService.ResolveAsync"/>
/// propagate unchanged — they are never mapped to a tenant rejection code (KTD4).
/// <see cref="IProblemDetailsCreator"/> is resolved lazily from <see cref="HttpContext.RequestServices"/>
/// inside the rejection branch only, rather than as a constructor dependency — a host that never rejects
/// (only ever resolves or no-ops) does not need <c>Headless.Api.Core</c>'s base ProblemDetails
/// infrastructure registered.
/// Once an identifier resolves, this middleware also enforces R19 mapping integrity against the default
/// authentication scheme for every such request — see <c>_RejectOnClaimMismatchAsync</c> for why that
/// cannot be left to <c>TenantIdentifierIntegrityHandler</c> alone.
/// </remarks>
internal sealed partial class TenantCatalogResolutionMiddleware(
    RequestDelegate next,
    IEnumerable<ITenantIdentifierSource> sources,
    IOptions<TenantCatalogOptions> options,
    IOptions<MultiTenancyOptions> tenancyOptions,
    ILogger<TenantCatalogResolutionMiddleware> logger
)
{
    // Fires exactly once per process for HEADLESS_TENANT_CATALOG_MIDDLEWARE_ORDERING. 0 = not yet
    // warned, 1 = warned. CompareExchange ensures the warning is emitted by at most one request.
    private static int _orderingWarningEmitted;

    /// <summary>Resolves the tenant identifier for the request and either sets the ambient tenant or rejects it.</summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="currentTenant">The current tenant accessor.</param>
    /// <param name="catalogService">The tenant catalog service resolved for the current request scope.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="context"/>, <paramref name="currentTenant"/>, or <paramref name="catalogService"/> is <see langword="null"/>.
    /// </exception>
    public async Task InvokeAsync(
        HttpContext context,
        ICurrentTenant currentTenant,
        ITenantCatalogService catalogService
    )
    {
        Argument.IsNotNull(context);
        Argument.IsNotNull(currentTenant);
        Argument.IsNotNull(catalogService);

        context.Features.Set(HeadlessTenancyResolutionApplied.Instance);

        var endpoint = context.GetEndpoint();

        if (endpoint is null)
        {
            // Endpoint metadata (SkipTenantResolution) is not resolvable before UseRouting() has run, so
            // resolution is skipped either way. But a null endpoint here is ambiguous: the consumer may
            // have placed this middleware ahead of UseRouting(), or routing may already have run and
            // simply not matched a route (an ordinary 404 probe). Only the former is a misconfiguration,
            // and the two are distinguishable after the fact — routing running downstream assigns an
            // endpoint. Deciding after next() keeps unmatched routes from consuming the one-shot warning.
            await next(context).ConfigureAwait(false);

            if (context.GetEndpoint() is not null)
            {
                _WarnIfMiddlewareLikelyMisordered();
            }

            return;
        }

        if (endpoint.Metadata.GetMetadata<SkipTenantResolutionAttribute>() is not null)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        string? identifier = null;

        foreach (var source in sources)
        {
            identifier = source.GetIdentifier(context);

            if (identifier is not null)
            {
                break;
            }
        }

        if (identifier is null)
        {
            // Zero sources registered, or every source returned null — no-op, host context (R5).
            await next(context).ConfigureAwait(false);
            return;
        }

        var outcome = await catalogService.ResolveAsync(identifier, context.RequestAborted).ConfigureAwait(false);

        switch (outcome.Kind)
        {
            case TenantResolutionKind.Ignored:
                await next(context).ConfigureAwait(false);
                return;

            case TenantResolutionKind.Resolved:
                var tenant = outcome.Tenant!;
                context.Features.Set(new TenantIdentifierResolvedFeature(tenant.Id));

                // The ambient scope opens BEFORE R19 enforcement: _RejectOnClaimMismatchAsync authenticates
                // the default scheme, and AuthenticationHandler<TOptions> caches that result for the whole
                // request — so a host deriving authentication configuration per tenant (signing keys,
                // issuer, authority) must observe the resolved tenant on that first authenticate call or
                // permanently observe host context, defeating the reason this middleware runs pre-auth.
                // The rejection writer reads no ambient tenant state, so rejection semantics are unchanged.
                using (currentTenant.Change(tenant.Id, tenant.Name))
                {
                    var rejected = await _RejectOnClaimMismatchAsync(
                            context,
                            tenant.Id,
                            tenancyOptions.Value,
                            options.Value.DetailedResolutionErrors
                        )
                        .ConfigureAwait(false);

                    if (rejected)
                    {
                        return;
                    }

                    await next(context).ConfigureAwait(false);
                }

                return;

            default:
                var problemDetailsCreator = context.RequestServices.GetRequiredService<IProblemDetailsCreator>();

                await TenantCatalogRejectionWriter
                    .RejectAsync(context, outcome.Kind, problemDetailsCreator, options.Value.DetailedResolutionErrors)
                    .ConfigureAwait(false);
                return;
        }
    }

    /// <summary>
    /// Test seam: resets the once-per-process ordering warning flag so that tests asserting on the
    /// HEADLESS_TENANT_CATALOG_MIDDLEWARE_ORDERING log entry can run in any order.
    /// </summary>
    internal static void ResetOrderingWarningForTesting()
    {
        Volatile.Write(ref _orderingWarningEmitted, 0);
    }

    /// <summary>
    /// Unconditional R19 enforcement (KTD2): rejects the request when the default-scheme principal
    /// carries a tenant claim that disagrees with the identifier-resolved tenant. Returns
    /// <see langword="true"/> when the response was written and the pipeline must short-circuit.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>TenantIdentifierIntegrityHandler</c> cannot be the only enforcement point:
    /// <c>AuthorizationMiddleware</c> skips authorization entirely for <c>[AllowAnonymous]</c> endpoints
    /// and for endpoints with no authorize metadata when no <c>FallbackPolicy</c> is configured, so its
    /// handlers never run for those requests. This check runs for every identifier-resolved request
    /// regardless of endpoint metadata or policy configuration.
    /// </para>
    /// <para>
    /// The principal is materialized here rather than read from <see cref="HttpContext.User"/> because
    /// this middleware is documented to run before <c>UseAuthentication()</c> (KTD2). Authenticating the
    /// default scheme costs nothing extra for hosts that call <c>UseAuthentication()</c>: that middleware
    /// authenticates the same scheme for every request regardless of endpoint metadata, and
    /// <c>AuthenticationHandler&lt;TOptions&gt;</c> caches its result for the lifetime of the request —
    /// so this is the same authentication call, made earlier. Hosts with no default authenticate scheme
    /// are skipped rather than forced to configure one. Because that cached result is what every later
    /// stage observes, the caller invokes this method from inside the resolved tenant's ambient scope:
    /// a host deriving authentication configuration per tenant would otherwise bind host context for the
    /// whole request.
    /// </para>
    /// <para>
    /// Endpoint-scoped (non-default) schemes are not materialized until <c>PolicyEvaluator</c> runs
    /// inside authorization, so they remain <c>TenantIdentifierIntegrityHandler</c>'s responsibility.
    /// This path writes the rejection directly and does not set
    /// <c>TenantIdentifierMismatchFeature</c> — that feature is the authorization handler's channel to
    /// <c>StatusCodesRewriterMiddleware</c>, and setting it here would invite a second write of an
    /// already-written response.
    /// </para>
    /// </remarks>
    private static async Task<bool> _RejectOnClaimMismatchAsync(
        HttpContext context,
        string resolvedTenantId,
        MultiTenancyOptions tenancyOptions,
        bool detailedResolutionErrors
    )
    {
        var schemeProvider = context.RequestServices.GetService<IAuthenticationSchemeProvider>();

        if (schemeProvider is null)
        {
            return false;
        }

        var defaultScheme = await schemeProvider.GetDefaultAuthenticateSchemeAsync().ConfigureAwait(false);

        if (defaultScheme is null)
        {
            return false;
        }

        var authenticateResult = await context.AuthenticateAsync(defaultScheme.Name).ConfigureAwait(false);

        if (
            !authenticateResult.Succeeded
            || !TenantIdentifierIntegrityChecker.IsMismatch(
                authenticateResult.Principal,
                resolvedTenantId,
                tenancyOptions
            )
        )
        {
            return false;
        }

        var problemDetailsCreator = context.RequestServices.GetRequiredService<IProblemDetailsCreator>();

        await TenantCatalogRejectionWriter
            .RejectMismatchAsync(context, problemDetailsCreator, detailedResolutionErrors)
            .ConfigureAwait(false);

        return true;
    }

    private void _WarnIfMiddlewareLikelyMisordered()
    {
        if (Volatile.Read(ref _orderingWarningEmitted) != 0)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _orderingWarningEmitted, 1, 0) != 0)
        {
            return;
        }

        LogMiddlewareOrderingWarning(logger);
    }

    [LoggerMessage(
        EventId = 1,
        EventName = "HEADLESS_TENANT_CATALOG_MIDDLEWARE_ORDERING",
        Level = LogLevel.Warning,
        Message = "UseHeadlessTenantCatalogResolution() observed a request with no resolved endpoint. "
            + "Place UseHeadlessTenantCatalogResolution() AFTER UseRouting() and BEFORE UseAuthentication(). "
            + "This warning is emitted once per process."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogMiddlewareOrderingWarning(ILogger logger);
}
