// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;
using Microsoft.Extensions.FileProviders;

namespace Headless.DevInbox.Dashboard;

/// <summary>
/// The Dev inbox module's UI build: its manifest, entry, and view chunks with their precompressed siblings, embedded
/// in this package from <c>wwwroot/dist/modules/dev-inbox</c>, which the module package's npm build writes.
/// </summary>
internal static class DevInboxAssets
{
    /// <summary>
    /// The manifest-resource prefix the package's build gives the module's output folder. MSBuild turns the
    /// <c>dev-inbox</c> folder into the <c>dev_inbox</c> segment of a resource name.
    /// </summary>
    public const string EmbeddedNamespace = "Headless.DevInbox.Dashboard.wwwroot.dist.modules.dev_inbox";

    public static DashboardModuleAssets Create()
    {
        return new DashboardModuleAssets
        {
            Files = new EmbeddedFileProvider(typeof(DevInboxAssets).Assembly, EmbeddedNamespace),
        };
    }
}
