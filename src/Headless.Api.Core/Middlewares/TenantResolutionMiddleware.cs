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

/// <summary>Resolves the current tenant from the authenticated principal for the lifetime of the HTTP request.</summary>
/// <remarks>
/// The R19 mismatch dependencies (<see cref="IProblemDetailsCreator"/>, <see cref="TenantCatalogOptions"/>)
/// are deliberately resolved from <see cref="HttpContext.RequestServices"/> inside the mismatch branch
/// rather than taken as constructor parameters — this middleware is usable standalone (claim resolution
/// with no catalog and no <c>Headless.Api.Core</c> base infrastructure registered), and a hard
/// constructor dependency on services that only the catalog integration needs would break that minimal
/// host for every request, not just the rare mismatch path.
/// </remarks>
internal sealed partial class TenantResolutionMiddleware(
    RequestDelegate next,
    IOptions<MultiTenancyOptions> options,
    ILogger<TenantResolutionMiddleware> logger
)
{
    // Fires exactly once per process for HEADLESS_TENANCY_MIDDLEWARE_ORDERING. 0 = not yet warned,
    // 1 = warned. CompareExchange ensures the warning is emitted by at most one request.
    private static int _orderingWarningEmitted;

    /// <summary>Resolves the tenant from the current user claims and restores the previous tenant when the request ends.</summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="currentTenant">The current tenant accessor.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="currentTenant"/> is <see langword="null"/>.</exception>
    public async Task InvokeAsync(HttpContext context, ICurrentTenant currentTenant)
    {
        Argument.IsNotNull(context);
        Argument.IsNotNull(currentTenant);

        context.Features.Set(HeadlessTenancyResolutionApplied.Instance);

        if (context.GetEndpoint()?.Metadata.GetMetadata<SkipTenantResolutionAttribute>() is not null)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            _WarnIfMiddlewareLikelyMisordered(context);
            await next(context).ConfigureAwait(false);
            return;
        }

        var tenantId = TenantClaimReader.GetTenantId(context.User, options.Value);

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        // R19 fast path: when catalog identifier resolution already ran for this request, the claim
        // must canonicalize to the same tenant. Compared against the preserved request feature — never
        // against ambient ICurrentTenant.Id, which this middleware's own Change() below would otherwise
        // have overwritten by the time a later check ran. The authoritative check is the post-authorization
        // TenantIdentifierIntegrityHandler (KTD2); this is a fast-path short-circuit only.
        var resolvedFeature = context.Features.Get<TenantIdentifierResolvedFeature>();

        if (resolvedFeature is not null && !string.Equals(resolvedFeature.TenantId, tenantId, StringComparison.Ordinal))
        {
            var problemDetailsCreator = context.RequestServices.GetRequiredService<IProblemDetailsCreator>();
            var catalogOptions = context.RequestServices.GetRequiredService<IOptions<TenantCatalogOptions>>();

            await TenantCatalogRejectionWriter
                .RejectMismatchAsync(context, problemDetailsCreator, catalogOptions.Value.DetailedResolutionErrors)
                .ConfigureAwait(false);
            return;
        }

        if (resolvedFeature is not null)
        {
            // The claim agrees with the catalog-resolved identifier — the mismatch branch above returned
            // otherwise — and TenantCatalogResolutionMiddleware already opened Change(tenant.Id, tenant.Name)
            // for this request. Opening a nested Change(tenantId) here would replace the whole ambient slot
            // with TenantInformation(tenantId, null) and drop the catalog-supplied display name (R6).
            await next(context).ConfigureAwait(false);
            return;
        }

        // Claim-only host: no catalog resolution ran for this request, so no display name is known.
        using var _ = currentTenant.Change(tenantId);
        await next(context).ConfigureAwait(false);
    }

    /// <summary>
    /// Test seam: resets the once-per-process ordering warning flag so that tests asserting on the
    /// HEADLESS_TENANCY_MIDDLEWARE_ORDERING log entry can run in any order.
    /// </summary>
    internal static void ResetOrderingWarningForTesting()
    {
        Volatile.Write(ref _orderingWarningEmitted, 0);
    }

    private void _WarnIfMiddlewareLikelyMisordered(HttpContext context)
    {
        // If AuthenticationMiddleware has not run yet, the consumer almost certainly placed
        // UseHeadlessTenancy() ahead of UseAuthentication(). Warn once per process to avoid log spam.
        if (context.Features.Get<IAuthenticationFeature>() is not null)
        {
            return;
        }

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
        EventId = 0,
        EventName = "HEADLESS_TENANCY_MIDDLEWARE_ORDERING",
        Level = LogLevel.Warning,
        Message = "UseHeadlessTenancy() observed an unauthenticated request. If your endpoints expect a "
            + "claims-resolved tenant, place UseHeadlessTenancy() AFTER UseAuthentication() (and before "
            + "UseAuthorization()). This warning is emitted once per process."
    )]
    // ReSharper disable once InconsistentNaming
    private static partial void LogMiddlewareOrderingWarning(ILogger logger);
}

/// <summary>
/// Marker feature set on the current HTTP request when <see cref="TenantResolutionMiddleware"/> has
/// executed for it. Consumed by <c>HeadlessApiExceptionHandler</c> to surface a runtime warning when
/// a <see cref="MissingTenantContextException"/> is raised on a request that never passed through
/// <c>UseHeadlessTenancy()</c>.
/// </summary>
internal sealed class HeadlessTenancyResolutionApplied
{
    public static HeadlessTenancyResolutionApplied Instance { get; } = new();

    private HeadlessTenancyResolutionApplied() { }
}
