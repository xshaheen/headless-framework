// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;
using Headless.Blobs.Internal;
using Headless.Checks;
using Headless.MultiTenancy;
using Headless.Primitives;

namespace Headless.Blobs;

/// <summary>
/// Rewrites every location, list query, and delete-all query with the ambient tenant before it reaches the wrapped
/// store, and strips the tenant back out of every key the store returns, so callers keep addressing logical
/// locations.
/// </summary>
/// <remarks>
/// It wraps the store rather than scoping inside each provider so that one implementation covers every provider and
/// every capability decorator (the signed-URL endpoint) underneath it.
/// </remarks>
internal class TenantScopedBlobStorage(IBlobStorage inner, TenantBlobScope scope) : IScopedBlobStorage
{
    private int _disposed;

    protected IBlobStorage Inner { get; } = inner;

    protected TenantBlobScope Scope { get; } = scope;

    public IBlobStorage Unscoped => Inner;

    public bool RequiresContainerProvisioning => Inner.RequiresContainerProvisioning;

    #region Upload

    public ValueTask UploadAsync(
        BlobLocation location,
        Stream content,
        IReadOnlyDictionary<string, string>? metadata = null,
        string? contentType = null,
        CancellationToken cancellationToken = default
    )
    {
        return Inner.UploadAsync(Scope.Apply(location), content, metadata, contentType, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<BlobBulkResult>> BulkUploadAsync(
        string container,
        IReadOnlyCollection<BlobUploadRequest> blobs,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(blobs);

        var scoped = Scope.Resolve(container);

        var results = new List<BlobBulkResult>(blobs.Count);
        var forwarded = new List<BlobUploadRequest>(blobs.Count);

        foreach (var blob in blobs)
        {
            if (_TryValidate(container, blob.Path, results))
            {
                forwarded.Add(blob with { Path = scoped.ApplyToPath(blob.Path) });
            }
        }

        if (forwarded.Count > 0)
        {
            var innerResults = await Inner
                .BulkUploadAsync(scoped.PhysicalContainer, forwarded, cancellationToken)
                .ConfigureAwait(false);

            results.AddRange(innerResults.Select(result => scoped.Strip(result, container)));
        }

        return results;
    }

    #endregion

    #region Delete

    public ValueTask<bool> DeleteAsync(BlobLocation location, CancellationToken cancellationToken = default)
    {
        return Inner.DeleteAsync(Scope.Apply(location), cancellationToken);
    }

    public async ValueTask<IReadOnlyList<BlobBulkResult>> BulkDeleteAsync(
        string container,
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(paths);

        var scoped = Scope.Resolve(container);

        var results = new List<BlobBulkResult>(paths.Count);
        var forwarded = new List<string>(paths.Count);

        foreach (var path in paths)
        {
            if (_TryValidate(container, path, results))
            {
                forwarded.Add(scoped.ApplyToPath(path));
            }
        }

        if (forwarded.Count > 0)
        {
            var innerResults = await Inner
                .BulkDeleteAsync(scoped.PhysicalContainer, forwarded, cancellationToken)
                .ConfigureAwait(false);

            results.AddRange(innerResults.Select(result => scoped.Strip(result, container)));
        }

        return results;
    }

    public ValueTask<int> DeleteAllAsync(BlobQuery query, CancellationToken cancellationToken = default)
    {
        return Inner.DeleteAllAsync(Scope.Apply(query), cancellationToken);
    }

    #endregion

    #region Move / Copy

    public ValueTask<bool> MoveAsync(
        BlobLocation source,
        BlobLocation destination,
        CancellationToken cancellationToken = default
    )
    {
        return Inner.MoveAsync(Scope.Apply(source), Scope.Apply(destination), cancellationToken);
    }

    public ValueTask<bool> CopyAsync(
        BlobLocation source,
        BlobLocation destination,
        CancellationToken cancellationToken = default
    )
    {
        return Inner.CopyAsync(Scope.Apply(source), Scope.Apply(destination), cancellationToken);
    }

    #endregion

    #region Exists / Download / Info

    public ValueTask<bool> ExistsAsync(BlobLocation location, CancellationToken cancellationToken = default)
    {
        return Inner.ExistsAsync(Scope.Apply(location), cancellationToken);
    }

    [MustDisposeResource]
    public async ValueTask<BlobDownloadResult?> OpenReadStreamAsync(
        BlobLocation location,
        CancellationToken cancellationToken = default
    )
    {
        var scoped = Scope.Resolve(location.Container);

        var result = await Inner.OpenReadStreamAsync(scoped.Apply(location), cancellationToken).ConfigureAwait(false);

        if (result is null)
        {
            return null;
        }

        try
        {
            return result with { FileName = scoped.Strip(result.FileName) };
        }
        catch
        {
            // The caller never receives the stream, so release it here instead of leaking a pooled connection.
            await result.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask<BlobInfo?> GetBlobInfoAsync(
        BlobLocation location,
        CancellationToken cancellationToken = default
    )
    {
        var scoped = Scope.Resolve(location.Container);

        var info = await Inner.GetBlobInfoAsync(scoped.Apply(location), cancellationToken).ConfigureAwait(false);

        return info is null ? null : scoped.Strip(info);
    }

    #endregion

    #region List

    public async ValueTask<BlobPage> ListAsync(BlobQuery query, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(query);

        var scoped = Scope.Resolve(query.Container);

        var page = await Inner.ListAsync(scoped.Apply(query), cancellationToken).ConfigureAwait(false);

        return page with
        {
            Items = page.Items.Select(scoped.Strip).ToList(),
        };
    }

    #endregion

    public ValueTask DisposeAsync()
    {
        // A named store is also resolved through its keyed IPresignedUrlBlobStorage forward, so the container can
        // dispose this instance twice. The wrapped store was built by the decorating factory, so this instance owns
        // releasing it exactly once.
        return Interlocked.Exchange(ref _disposed, 1) == 0 ? Inner.DisposeAsync() : ValueTask.CompletedTask;
    }

    /// <summary>
    /// Validates a raw bulk path the way the providers do, recording a per-entry failure for an unaddressable path.
    /// Prefixing the tenant could otherwise turn an invalid path such as <c>""</c> into a valid key.
    /// </summary>
    private static bool _TryValidate(string container, string path, List<BlobBulkResult> results)
    {
        try
        {
            _ = new BlobLocation(container, path);

            return true;
        }
        catch (ArgumentException e)
        {
            results.Add(new BlobBulkResult(container, path, Result<bool, Exception>.Fail(e)));

            return false;
        }
    }
}
