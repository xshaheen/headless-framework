// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Options;

namespace Headless.Dashboard;

/// <summary>
/// Merges the registered modules' manifests once and caches the result for every request. The merge reads only
/// resources embedded in the module assemblies, so running it at startup costs no I/O.
/// </summary>
internal sealed class DashboardManifestProvider(
    DashboardModuleCatalog catalog,
    IOptions<HeadlessDashboardOptions> options
)
{
    private readonly Lazy<MergedManifest> _manifest = new(() =>
        ManifestMerger.Merge(catalog.Modules, ManifestMerger.ShellSdkVersion, options.Value.Contributions)
    );

    /// <summary>The merged manifest.</summary>
    /// <exception cref="InvalidOperationException">A module's manifest is missing or invalid.</exception>
    public MergedManifest Manifest => _manifest.Value;
}
