// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace Headless.Jobs;

/// <summary>
/// The Jobs module's UI build, embedded from <c>wwwroot/dist/module</c>. A build without that folder serves a
/// server-only manifest instead, so the module's commands, Needs you, Overview, and stream work before its views ship.
/// </summary>
internal static class JobsAssets
{
    /// <summary>The manifest-resource prefix the package's build gives the module's output folder.</summary>
    public const string EmbeddedNamespace = "Headless.Jobs.wwwroot.dist.module";

    public static DashboardModuleAssets Create()
    {
        var embedded = new EmbeddedFileProvider(typeof(JobsAssets).Assembly, EmbeddedNamespace);

        return new DashboardModuleAssets
        {
            Files = embedded.GetFileInfo(ModuleManifest.FileName).Exists
                ? embedded
                : new ServerOnlyManifestFileProvider(embedded),
        };
    }

    private sealed class ServerOnlyManifestFileProvider(IFileProvider inner) : IFileProvider
    {
        private static readonly byte[] _Manifest = Encoding.UTF8.GetBytes(
            """
            {
              "id": "jobs",
              "sdkVersion": "1.0.0",
              "entry": "index.js",
              "objectKinds": { "run": {}, "cron-job": {}, "function": {}, "node": {} }
            }
            """
        );

        public IFileInfo GetFileInfo(string subpath)
        {
            return string.Equals(subpath.TrimStart('/'), ModuleManifest.FileName, StringComparison.Ordinal)
                ? new ManifestFile()
                : inner.GetFileInfo(subpath);
        }

        public IDirectoryContents GetDirectoryContents(string subpath) => inner.GetDirectoryContents(subpath);

        public IChangeToken Watch(string filter) => inner.Watch(filter);

        private sealed class ManifestFile : IFileInfo
        {
            public bool Exists => true;

            public long Length => _Manifest.Length;

            public string? PhysicalPath => null;

            public string Name => ModuleManifest.FileName;

            public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;

            public bool IsDirectory => false;

            public Stream CreateReadStream() => new MemoryStream(_Manifest, writable: false);
        }
    }
}
