// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Headless.Dashboard;

/// <summary>
/// Serves the shell's and the modules' embedded build files: content-hashed, precompressed, and cached for a year.
/// </summary>
/// <remarks>
/// The shell serves these itself instead of through static web assets, because a library's static web assets live at
/// a fixed <c>_content/{PackageId}</c> path that cannot follow a base path or <c>PathBase</c> chosen at run time.
/// A file's Brotli (<c>.br</c>) or gzip (<c>.gz</c>) sibling, written by the front-end build, is served instead of
/// the file when the request accepts that encoding.
/// </remarks>
internal static class EmbeddedAssetEndpoints
{
    /// <summary>The cache policy of every build file: its name or URL changes whenever its content does.</summary>
    public const string ImmutableCacheControl = "public, max-age=31536000, immutable";

    private static readonly FileExtensionContentTypeProvider _ContentTypes = new();

    /// <summary>
    /// Writes the file at <paramref name="subpath"/> in <paramref name="assets"/>, a <c>304</c> when the request's
    /// <c>If-None-Match</c> already names it, or a <c>404</c> when no such file exists.
    /// </summary>
    public static Task ServeAsync(HttpContext context, EmbeddedAssetSet assets, string subpath)
    {
        var request = context.Request;
        var response = context.Response;
        var identity = assets.Find(subpath);

        if (identity is null)
        {
            response.StatusCode = StatusCodes.Status404NotFound;

            return Task.CompletedTask;
        }

        var (asset, encoding) = _Negotiate(request, assets, subpath, identity);
        var headers = response.Headers;

        // Vary goes on every answer, compressed or not, so a shared cache never hands a Brotli body to a client that
        // asked for identity.
        headers.Vary = HeaderNames.AcceptEncoding;
        headers.CacheControl = ImmutableCacheControl;
        headers.ETag = asset.ETag.ToString();
        headers.XContentTypeOptions = "nosniff";

        if (_IsNotModified(request, asset.ETag))
        {
            response.StatusCode = StatusCodes.Status304NotModified;

            return Task.CompletedTask;
        }

        response.ContentType = _ContentTypes.TryGetContentType(subpath, out var contentType)
            ? contentType
            : "application/octet-stream";

        if (encoding is not null)
        {
            headers.ContentEncoding = encoding;
        }

        response.ContentLength = asset.File.Length;

        return HttpMethods.IsHead(request.Method)
            ? Task.CompletedTask
            : response.SendFileAsync(asset.File, context.RequestAborted);
    }

    private static (EmbeddedAsset Asset, string? Encoding) _Negotiate(
        HttpRequest request,
        EmbeddedAssetSet assets,
        string subpath,
        EmbeddedAsset identity
    )
    {
        var accepted = request.GetTypedHeaders().AcceptEncoding;

        if (accepted.Count == 0)
        {
            return (identity, null);
        }

        if (_Accepts(accepted, "br") && assets.Find(subpath + ".br") is { } brotli)
        {
            return (brotli, "br");
        }

        if (_Accepts(accepted, "gzip") && assets.Find(subpath + ".gz") is { } gzip)
        {
            return (gzip, "gzip");
        }

        return (identity, null);
    }

    private static bool _Accepts(IList<StringWithQualityHeaderValue> accepted, string encoding)
    {
        foreach (var value in accepted)
        {
            if (StringSegment.Equals(value.Value, encoding, StringComparison.OrdinalIgnoreCase))
            {
                return (value.Quality ?? 1) > 0;
            }
        }

        return false;
    }

    private static bool _IsNotModified(HttpRequest request, EntityTagHeaderValue etag)
    {
        foreach (var candidate in request.GetTypedHeaders().IfNoneMatch)
        {
            if (candidate.Equals(EntityTagHeaderValue.Any) || candidate.Compare(etag, useStrongComparison: false))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// One root of embedded build files, such as the shell's <c>dist</c> or one module's build, with each found file's
/// ETag computed once from its content.
/// </summary>
internal sealed class EmbeddedAssetSet(IFileProvider files)
{
    // Only files that exist are cached, so requests for made-up paths cannot grow the cache.
    private readonly ConcurrentDictionary<string, EmbeddedAsset> _found = new(StringComparer.Ordinal);

    /// <summary>The provider rooted at the build output.</summary>
    public IFileProvider Files { get; } = files;

    /// <summary>
    /// Returns the file at <paramref name="subpath"/>, a forward-slash path relative to the root, or
    /// <see langword="null"/> when it does not exist or the path is not a plain relative path.
    /// </summary>
    public EmbeddedAsset? Find(string subpath)
    {
        if (_found.TryGetValue(subpath, out var cached))
        {
            return cached;
        }

        if (!_IsPlainRelativePath(subpath))
        {
            return null;
        }

        var file = Files.GetFileInfo(subpath);

        if (!file.Exists || file.IsDirectory)
        {
            return null;
        }

        return _found.GetOrAdd(subpath, static (_, found) => EmbeddedAsset.Create(found), file);
    }

    // A module may hand the shell any IFileProvider, so a traversal segment is refused here rather than trusted to
    // the provider.
    private static bool _IsPlainRelativePath(string subpath)
    {
        if (subpath.Length == 0 || subpath[0] == '/' || subpath.Contains('\\', StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var segment in subpath.Split('/'))
        {
            if (segment is "" or "." or "..")
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>A found build file and its content version.</summary>
internal sealed class EmbeddedAsset
{
    private EmbeddedAsset(IFileInfo file, string version)
    {
        File = file;
        Version = version;
        ETag = new EntityTagHeaderValue($"\"{version}\"");
    }

    public IFileInfo File { get; }

    /// <summary>A short hash of the file's content, URL-safe: the ETag's value and a URL's cache-busting version.</summary>
    public string Version { get; }

    public EntityTagHeaderValue ETag { get; }

    public static EmbeddedAsset Create(IFileInfo file)
    {
        // Hashing the content, not the build time, keeps the ETag equal on every node that serves the same build.
        using var stream = file.CreateReadStream();
        var hash = SHA256.HashData(stream);

        return new EmbeddedAsset(file, Base64Url.EncodeToString(hash.AsSpan(0, 12)));
    }
}
