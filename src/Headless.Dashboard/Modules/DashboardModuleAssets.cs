// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.FileProviders;

namespace Headless.Dashboard;

/// <summary>
/// A module's embedded UI build: the files its Vite build wrote, with <c>manifest.json</c> at the root beside the
/// entry, its chunks, and its styles.
/// </summary>
/// <remarks>
/// The shell reads <c>manifest.json</c> once, when it builds the dashboard's manifest, and serves the other files
/// from its own endpoints. Use a provider over resources embedded in the module's assembly, such as
/// <see cref="ManifestEmbeddedFileProvider"/>, so reading the manifest is an in-process read and never touches disk
/// or network while the host starts. This record grows by <see langword="init"/> members.
/// </remarks>
[PublicAPI]
public sealed record DashboardModuleAssets
{
    /// <summary>Gets the provider rooted at the module's build output.</summary>
    public required IFileProvider Files { get; init; }
}
