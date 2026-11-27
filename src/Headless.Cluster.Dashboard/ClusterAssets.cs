// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;
using Microsoft.Extensions.FileProviders;

namespace Headless.Cluster;

/// <summary>
/// The Cluster module's UI build: its manifest, entry, and view chunks with their precompressed siblings, embedded in
/// this package from <c>wwwroot/dist/modules/cluster</c>, which the module package's npm build writes.
/// </summary>
internal static class ClusterAssets
{
    /// <summary>The manifest-resource prefix the package's build gives the module's output folder.</summary>
    public const string EmbeddedNamespace = "Headless.Cluster.wwwroot.dist.modules.cluster";

    public static DashboardModuleAssets Create()
    {
        return new DashboardModuleAssets
        {
            Files = new EmbeddedFileProvider(typeof(ClusterAssets).Assembly, EmbeddedNamespace),
        };
    }
}
