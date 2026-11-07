// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;
using Microsoft.Extensions.FileProviders;

namespace Headless.Jobs;

/// <summary>
/// The Jobs module's UI build: its manifest, entry, and view chunks with their precompressed siblings, embedded in
/// this package from <c>wwwroot/dist/modules/jobs</c>, which the module package's npm build writes.
/// </summary>
internal static class JobsAssets
{
    /// <summary>The manifest-resource prefix the package's build gives the module's output folder.</summary>
    public const string EmbeddedNamespace = "Headless.Jobs.wwwroot.dist.modules.jobs";

    public static DashboardModuleAssets Create()
    {
        return new DashboardModuleAssets
        {
            Files = new EmbeddedFileProvider(typeof(JobsAssets).Assembly, EmbeddedNamespace),
        };
    }
}
