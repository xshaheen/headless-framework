// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;
using Microsoft.Extensions.FileProviders;

namespace Headless.Leases;

/// <summary>
/// The Leases module's UI build: its manifest, entry, and view chunks with their precompressed siblings, embedded in
/// this package from <c>wwwroot/dist/modules/leases</c>, which the module package's npm build writes.
/// </summary>
internal static class LeasesAssets
{
    /// <summary>The manifest-resource prefix the package's build gives the module's output folder.</summary>
    public const string EmbeddedNamespace = "Headless.Leases.wwwroot.dist.modules.leases";

    public static DashboardModuleAssets Create()
    {
        return new DashboardModuleAssets
        {
            Files = new EmbeddedFileProvider(typeof(LeasesAssets).Assembly, EmbeddedNamespace),
        };
    }
}
