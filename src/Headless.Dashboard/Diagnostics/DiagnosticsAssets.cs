// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace Headless.Dashboard;

/// <summary>The diagnostic module's UI build, embedded in this package from <c>wwwroot/dist/modules/diagnostics</c>.</summary>
internal static class DiagnosticsAssets
{
    /// <summary>The manifest-resource prefix the package's build gives the module's output folder.</summary>
    public const string EmbeddedNamespace = DashboardShellAssets.EmbeddedNamespace + ".modules.diagnostics";

    /// <summary>
    /// Returns the build for the instance <paramref name="id"/>. The build is written once, for
    /// <see cref="DiagnosticsOptions.DefaultId"/>; another instance serves the same files with its manifest's id and
    /// contribution ids moved to its own id, because the shell requires a manifest to name the module that ships it.
    /// </summary>
    public static DashboardModuleAssets Create(string id)
    {
        IFileProvider files = new EmbeddedFileProvider(typeof(DiagnosticsAssets).Assembly, EmbeddedNamespace);

        if (!string.Equals(id, DiagnosticsOptions.DefaultId, StringComparison.Ordinal))
        {
            files = new RenamedManifestFileProvider(files, id);
        }

        return new DashboardModuleAssets { Files = files };
    }
}

/// <summary>
/// Serves a module build unchanged except for <c>manifest.json</c>, whose id, and the module prefix of every
/// contribution id and event name in it, it moves to another module id.
/// </summary>
internal sealed class RenamedManifestFileProvider(IFileProvider inner, string id) : IFileProvider
{
    private readonly Lazy<IFileInfo> _manifest = new(() => _Rename(inner.GetFileInfo(ModuleManifest.FileName), id));

    public IFileInfo GetFileInfo(string subpath)
    {
        return string.Equals(subpath.TrimStart('/'), ModuleManifest.FileName, StringComparison.Ordinal)
            ? _manifest.Value
            : inner.GetFileInfo(subpath);
    }

    public IDirectoryContents GetDirectoryContents(string subpath) => inner.GetDirectoryContents(subpath);

    public IChangeToken Watch(string filter) => inner.Watch(filter);

    private static IFileInfo _Rename(IFileInfo source, string id)
    {
        if (!source.Exists || source.IsDirectory)
        {
            return source;
        }

        JsonNode? root;

        using (var stream = source.CreateReadStream())
        {
            root = JsonNode.Parse(stream);
        }

        if (root is not JsonObject manifest)
        {
            return source;
        }

        var prefix = manifest["id"]?.GetValue<string>() + ".";
        manifest["id"] = id;

        foreach (var section in (manifest["sidebar"] as JsonArray)?.OfType<JsonObject>() ?? [])
        {
            _RenameIds(section["entries"] as JsonArray, prefix, id);
        }

        _RenameIds(manifest["commands"] as JsonArray, prefix, id);

        if (manifest["events"] is JsonArray events)
        {
            for (var i = 0; i < events.Count; i++)
            {
                if (events[i]?.GetValue<string>() is { } name)
                {
                    events[i] = _Move(name, prefix, id);
                }
            }
        }

        return new MemoryFileInfo(
            ModuleManifest.FileName,
            Encoding.UTF8.GetBytes(manifest.ToJsonString()),
            source.LastModified
        );
    }

    private static void _RenameIds(JsonArray? entries, string prefix, string id)
    {
        foreach (var entry in entries?.OfType<JsonObject>() ?? [])
        {
            if (entry["id"]?.GetValue<string>() is { } entryId)
            {
                entry["id"] = _Move(entryId, prefix, id);
            }
        }
    }

    private static string _Move(string value, string prefix, string id)
    {
        return value.StartsWith(prefix, StringComparison.Ordinal) ? id + "." + value[prefix.Length..] : value;
    }

    private sealed class MemoryFileInfo(string name, byte[] content, DateTimeOffset lastModified) : IFileInfo
    {
        public bool Exists => true;

        public long Length => content.Length;

        public string? PhysicalPath => null;

        public string Name => name;

        public DateTimeOffset LastModified => lastModified;

        public bool IsDirectory => false;

        public Stream CreateReadStream() => new MemoryStream(content, writable: false);
    }
}
