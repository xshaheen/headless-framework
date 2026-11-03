// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Api;

/// <summary>
/// An output-cache base policy that adds the resolved tenant to every cache key. ASP.NET Core output caching keys an
/// entry by host, path, query, and the policy's vary-by rules and ignores the response <c>Vary</c> header, so a tenant
/// selected by a request header (or any source outside the URL) would otherwise share one entry across tenants.
/// </summary>
/// <remarks>
/// It only adds a vary-by value: it never turns caching on for an endpoint without its own output-cache policy. It
/// reads the ambient tenant, so <c>UseOutputCache()</c> must run after <c>UseHeadlessTenantCatalogResolution()</c>;
/// a request that reaches it before resolution ran is logged once per process at Error.
/// </remarks>
internal sealed partial class TenantOutputCachePolicy(ILogger<TenantOutputCachePolicy> logger) : IOutputCachePolicy
{
    /// <summary>The vary-by value name that carries the tenant id (empty for host context).</summary>
    public const string VaryByValueName = "Headless.Tenant";

    // 0 = not yet logged, 1 = logged; the misordering is a pipeline fact, so one event per process is enough.
    private static int _misorderedLogged;

    public ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        var httpContext = context.HttpContext;

        if (httpContext.Features.Get<HeadlessTenancyResolutionApplied>() is null)
        {
            _LogMisorderedOnce();
        }

        var tenantId = httpContext.RequestServices.GetService<ICurrentTenant>()?.Id;
        context.CacheVaryByRules.VaryByValues[VaryByValueName] = tenantId ?? string.Empty;

        return ValueTask.CompletedTask;
    }

    public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }

    private void _LogMisorderedOnce()
    {
        if (Volatile.Read(ref _misorderedLogged) != 0 || Interlocked.CompareExchange(ref _misorderedLogged, 1, 0) != 0)
        {
            return;
        }

        LogOutputCacheBeforeTenantResolution(logger);
    }

    [LoggerMessage(
        EventId = 1,
        EventName = "HEADLESS_TENANT_OUTPUT_CACHE_MISORDERED",
        Level = LogLevel.Error,
        Message = "Output caching ran before tenant catalog resolution, so cached responses cannot vary by tenant and "
            + "one tenant's response can be served to another. Place UseOutputCache() after "
            + "UseHeadlessTenantCatalogResolution()."
    )]
    private static partial void LogOutputCacheBeforeTenantResolution(ILogger logger);
}
