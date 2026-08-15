// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Api.MultiTenancy;
using Headless.Checks;
using Headless.MultiTenancy;
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
/// </remarks>
internal sealed partial class TenantCatalogResolutionMiddleware(
    RequestDelegate next,
    IEnumerable<ITenantIdentifierSource> sources,
    IOptions<TenantCatalogOptions> options,
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
            // Endpoint metadata (SkipTenantResolution) is not resolvable before UseRouting() has run —
            // the consumer almost certainly placed this middleware ahead of UseRouting(). Warn once per
            // process and continue without resolution rather than guessing.
            _WarnIfMiddlewareLikelyMisordered();
            await next(context).ConfigureAwait(false);
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

                using (currentTenant.Change(tenant.Id, tenant.Name))
                {
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
