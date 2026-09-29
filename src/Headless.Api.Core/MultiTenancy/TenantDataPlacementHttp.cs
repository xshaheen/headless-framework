// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.MultiTenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Api.MultiTenancy;

/// <summary>
/// Runs the rest of the request with the resolved tenant's data placement preloaded, so a tenant-routed context
/// injected into an endpoint or its services can be built. A pass-through when data placement is not configured.
/// </summary>
internal static class TenantDataPlacementHttp
{
    public static Task RunAsync(HttpContext context, string tenantId, Func<Task> next) =>
        context.RequestServices.GetService<TenantDataPlacementPreloader>() is { } preloader
            ? preloader.RunAsync(context.RequestServices, tenantId, next, context.RequestAborted)
            : next();
}
