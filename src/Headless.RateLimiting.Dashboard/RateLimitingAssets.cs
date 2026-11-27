// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;
using Microsoft.Extensions.FileProviders;

namespace Headless.RateLimiting;

/// <summary>
/// The Rate limits module's UI build: its manifest, entry, and view chunk with their precompressed siblings, embedded
/// in this package from <c>wwwroot/dist/modules/ratelimiting</c>, which the module package's npm build writes.
/// </summary>
internal static class RateLimitingAssets
{
    /// <summary>The manifest-resource prefix the package's build gives the module's output folder.</summary>
    public const string EmbeddedNamespace = "Headless.RateLimiting.wwwroot.dist.modules.ratelimiting";

    public static DashboardModuleAssets Create()
    {
        return new DashboardModuleAssets
        {
            Files = new EmbeddedFileProvider(typeof(RateLimitingAssets).Assembly, EmbeddedNamespace),
        };
    }
}
