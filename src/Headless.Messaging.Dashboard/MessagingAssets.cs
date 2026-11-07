// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;
using Microsoft.Extensions.FileProviders;

namespace Headless.Messaging.Dashboard;

/// <summary>
/// The Messaging module's UI build, embedded in this package from <c>ModuleUi</c>. It holds the module's
/// <c>manifest.json</c>: its product, its sidebar with the count keys <see cref="MessagingCounts"/> answers, and its
/// object kinds. The module's front end adds its entry and route chunks beside it.
/// </summary>
internal static class MessagingAssets
{
    /// <summary>The manifest-resource prefix the package's build gives the module's UI folder.</summary>
    public const string EmbeddedNamespace = "Headless.Messaging.Dashboard.ModuleUi";

    public static DashboardModuleAssets Create()
    {
        return new DashboardModuleAssets
        {
            Files = new EmbeddedFileProvider(typeof(MessagingAssets).Assembly, EmbeddedNamespace),
        };
    }
}
