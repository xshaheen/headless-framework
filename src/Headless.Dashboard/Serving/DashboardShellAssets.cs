// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.FileProviders;

namespace Headless.Dashboard;

/// <summary>The shell's own front-end build: the document template, <c>shell.json</c>, and <c>assets/</c>.</summary>
internal sealed class DashboardShellAssets(IFileProvider files)
{
    /// <summary>
    /// The manifest-resource prefix the package's build gives <c>wwwroot/dist</c>: the root namespace, then the folder
    /// path with its slashes as dots.
    /// </summary>
    public const string EmbeddedNamespace = "Headless.Dashboard.wwwroot.dist";

    /// <summary>The provider rooted at the build output.</summary>
    public IFileProvider Files { get; } = files;

    /// <summary>Returns the build embedded in this package.</summary>
    public static DashboardShellAssets CreateEmbedded()
    {
        return new DashboardShellAssets(
            new EmbeddedFileProvider(typeof(DashboardShellAssets).Assembly, EmbeddedNamespace)
        );
    }
}

/// <summary>The build files the dashboard serves: the shell's, and each loaded module's.</summary>
internal sealed class DashboardAssetCatalog(DashboardShellAssets shell, DashboardManifestProvider manifest)
{
    // A refused module gets no set, so none of its files is served.
    private readonly Lazy<FrozenDictionary<string, EmbeddedAssetSet>> _modules = new(() =>
        manifest
            .Manifest.Modules.Where(static module => !module.IsRefused)
            .ToFrozenDictionary(
                static module => module.Id,
                static module => new EmbeddedAssetSet(module.Module.Assets.Files),
                StringComparer.Ordinal
            )
    );

    /// <summary>The shell's build files.</summary>
    public EmbeddedAssetSet Shell { get; } = new(shell.Files);

    /// <summary>
    /// Returns the build files of the module with id <paramref name="moduleId"/>, or <see langword="null"/> when no
    /// such module is registered or the shell refused it.
    /// </summary>
    public EmbeddedAssetSet? FindModule(string moduleId)
    {
        return _modules.Value.GetValueOrDefault(moduleId);
    }
}
