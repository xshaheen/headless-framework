// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;
using Microsoft.Extensions.FileProviders;

namespace Headless.Messaging.Dashboard;

/// <summary>
/// The Messaging module's UI build: its manifest, entry, and view chunks with their precompressed siblings, embedded
/// in this package from <c>wwwroot/dist/modules/messaging</c>, which the module package's npm build writes. The
/// manifest's sidebar carries the count keys <see cref="MessagingCounts"/> answers.
/// </summary>
internal static class MessagingAssets
{
    /// <summary>The manifest-resource prefix the package's build gives the module's output folder.</summary>
    public const string EmbeddedNamespace = "Headless.Messaging.Dashboard.wwwroot.dist.modules.messaging";

    public static DashboardModuleAssets Create()
    {
        return new DashboardModuleAssets
        {
            Files = new EmbeddedFileProvider(typeof(MessagingAssets).Assembly, EmbeddedNamespace),
        };
    }
}
