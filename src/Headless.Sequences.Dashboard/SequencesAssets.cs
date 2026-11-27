// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;
using Microsoft.Extensions.FileProviders;

namespace Headless.Sequences;

/// <summary>
/// The Sequences module's UI build: its manifest, entry, and view chunk with their precompressed siblings, embedded in
/// this package from <c>wwwroot/dist/modules/sequences</c>, which the module package's npm build writes.
/// </summary>
internal static class SequencesAssets
{
    /// <summary>The manifest-resource prefix the package's build gives the module's output folder.</summary>
    public const string EmbeddedNamespace = "Headless.Sequences.wwwroot.dist.modules.sequences";

    public static DashboardModuleAssets Create()
    {
        return new DashboardModuleAssets
        {
            Files = new EmbeddedFileProvider(typeof(SequencesAssets).Assembly, EmbeddedNamespace),
        };
    }
}
