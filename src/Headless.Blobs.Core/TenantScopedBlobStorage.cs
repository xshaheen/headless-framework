// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;
using Headless.Blobs.Internals;
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
internal class TenantScopedBlobStorage(IBlobStorage inner, TenantBlobScope scope) : IBlobStorage
{
    private int _disposed;

    protected IBlobStorage Inner { get; } = inner;

    protected TenantBlobScope Scope { get; } = scope;

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

        if (Scope.Resolve(container) is not { } scoped)
        {
            return await Inner.BulkUploadAsync(container, blobs, cancellationToken).ConfigureAwait(false);
        }

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

        if (Scope.Resolve(container) is not { } scoped)
        {
            return await Inner.BulkDeleteAsync(container, paths, cancellationToken).ConfigureAwait(false);
        }

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
        if (Scope.Resolve(location.Container) is not { } scoped)
        {
            return await Inner.OpenReadStreamAsync(location, cancellationToken).ConfigureAwait(false);
        }

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
        if (Scope.Resolve(location.Container) is not { } scoped)
        {
            return await Inner.GetBlobInfoAsync(location, cancellationToken).ConfigureAwait(false);
        }

        var info = await Inner.GetBlobInfoAsync(scoped.Apply(location), cancellationToken).ConfigureAwait(false);

        return info is null ? null : scoped.Strip(info);
    }

    #endregion

    #region List

    public async ValueTask<BlobPage> ListAsync(BlobQuery query, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(query);

        if (Scope.Resolve(query.Container) is not { } scoped)
        {
            return await Inner.ListAsync(query, cancellationToken).ConfigureAwait(false);
        }

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
#pragma warning disable ERP022 // The failure is recorded as that entry's result, matching the providers' bulk contract.
        catch (ArgumentException e)
        {
            results.Add(new BlobBulkResult(container, path, Result<bool, Exception>.Fail(e)));

            return false;
        }
#pragma warning restore ERP022
    }
}

/// <summary>
/// <see cref="TenantScopedBlobStorage"/> over a store that can presign, so <c>storage is IPresignedUrlBlobStorage</c>
/// keeps answering the same way once the store is scoped.
/// </summary>
internal sealed class TenantScopedPresignedBlobStorage(IBlobStorage inner, TenantBlobScope scope)
    : TenantScopedBlobStorage(inner, scope),
        IPresignedUrlBlobStorage
{
    private IPresignedUrlBlobStorage PresignedInner => (IPresignedUrlBlobStorage)Inner;

    public PresignedUploadConstraintKinds SupportedUploadConstraints => PresignedInner.SupportedUploadConstraints;

    public ValueTask<Uri> GetPresignedDownloadUrlAsync(
        BlobLocation location,
        TimeSpan expiry,
        CancellationToken cancellationToken = default
    )
    {
        return PresignedInner.GetPresignedDownloadUrlAsync(Scope.Apply(location), expiry, cancellationToken);
    }

    public ValueTask<Uri> GetPresignedUploadUrlAsync(
        BlobLocation location,
        TimeSpan expiry,
        PresignedUploadConstraints? constraints = null,
        CancellationToken cancellationToken = default
    )
    {
        return PresignedInner.GetPresignedUploadUrlAsync(Scope.Apply(location), expiry, constraints, cancellationToken);
    }
}

/// <summary>Maps a logical container to the physical container and key prefix of the ambient tenant.</summary>
internal sealed partial class TenantBlobScope(
    TenantBlobScopingStrategy strategy,
    string containerPrefix,
    ICurrentTenant currentTenant,
    ITenantStorageScopeBypass bypass
)
{
    /// <summary>The longest container name S3, R2, and Azure accept; longer names are truncated by their normalizers.</summary>
    public const int MaxContainerNameLength = 63;

    private const int _MinContainerNameLength = 3;

    /// <summary>
    /// Resolves the scope for <paramref name="container"/>, or <see langword="null"/> when a bypass is active and the
    /// operation must pass through unchanged.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="container"/> is not a valid container.</exception>
    /// <exception cref="MissingTenantContextException">No bypass is active and no ambient tenant is set.</exception>
    /// <exception cref="InvalidOperationException">The ambient tenant id cannot be used as a storage segment.</exception>
    public ScopedContainer? Resolve(string container)
    {
        // Validate before the bypass check so an invalid container fails the same way scoped or not; under
        // ContainerPerTenant the container never reaches a provider as a container, so nothing else would check it.
        Argument.IsNotNullOrWhiteSpace(container);
        PathValidation.ValidatePathSegment(container);

        if (bypass.IsActive)
        {
            return null;
        }

        var tenantId = currentTenant.Id;

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new MissingTenantContextException(
                "Tenant-scoped blob storage was used with no ambient tenant. Wrap the call in "
                    + "ICurrentTenant.Change(tenantId), or use ITenantStorageScopeBypass.BeginBypass() for an "
                    + "intentional host-level blob."
            );
        }

        return strategy == TenantBlobScopingStrategy.ContainerPerTenant
            ? new ScopedContainer(_TenantContainer(tenantId), _ContainerSegment(container) + "/")
            : new ScopedContainer(container, _TenantSegment(tenantId) + "/");
    }

    public BlobLocation Apply(BlobLocation location)
    {
        return Resolve(location.Container) is { } scoped ? scoped.Apply(location) : location;
    }

    public BlobQuery Apply(BlobQuery query)
    {
        Argument.IsNotNull(query);

        return Resolve(query.Container) is { } scoped ? scoped.Apply(query) : query;
    }

    private static string _TenantSegment(string tenantId)
    {
        // An allow-list rather than a deny-list: a segment the filesystem-like normalizers would strip or split
        // ('a:b' becomes 'ab', 'a/b' becomes two segments) would land one tenant inside another tenant's prefix.
        if (!TenantSegmentRegex.IsMatch(tenantId) || BlobStorageHelpers.HasSidecarSegment(tenantId))
        {
            throw new InvalidOperationException(
                "Tenant-scoped blob storage cannot use this tenant id as a path segment. A tenant id must start and "
                    + "end with an ASCII letter or digit and contain only ASCII letters, digits, '.', '_', and '-'."
            );
        }

        return tenantId;
    }

    private string _TenantContainer(string tenantId)
    {
        var name = containerPrefix + tenantId;

        // Provider normalizers lowercase, strip, and truncate container names, so a name outside this shape could
        // normalize to another tenant's container.
        if (name.Length is < _MinContainerNameLength or > MaxContainerNameLength || !TenantContainerRegex.IsMatch(name))
        {
            throw new InvalidOperationException(
                "Tenant-scoped blob storage cannot name a container for this tenant id. The container name "
                    + "(ContainerPrefix + tenant id) must be 3-63 characters of lowercase ASCII letters, digits, and "
                    + "single hyphens, starting and ending with a letter or digit."
            );
        }

        return name;
    }

    private static string _ContainerSegment(string container)
    {
        // The logical container becomes the first key segment, so a separator inside it would let "a/b" + "c" and
        // "a" + "b/c" address the same key.
        if (container.Contains('/', StringComparison.Ordinal) || container.Contains('\\', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A container cannot contain '/' or '\\' when blobs are scoped with a container per tenant.",
                nameof(container)
            );
        }

        return container;
    }

    [GeneratedRegex("^[A-Za-z0-9](?:[A-Za-z0-9._-]*[A-Za-z0-9])?$", RegexOptions.CultureInvariant, 100)]
    private static partial Regex TenantSegmentRegex { get; }

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant, 100)]
    private static partial Regex TenantContainerRegex { get; }
}

/// <summary>The physical container and key prefix one logical container maps to for the ambient tenant.</summary>
internal readonly record struct ScopedContainer(string PhysicalContainer, string KeyPrefix)
{
    public BlobLocation Apply(BlobLocation location)
    {
        return new BlobLocation(PhysicalContainer, KeyPrefix + location.Path);
    }

    public BlobQuery Apply(BlobQuery query)
    {
        return new BlobQuery(
            PhysicalContainer,
            KeyPrefix + query.Prefix,
            query.PageSize,
            query.ContinuationToken,
            query.IncludeMetadata
        );
    }

    public string ApplyToPath(string path)
    {
        return KeyPrefix + path;
    }

    /// <summary>Removes the key prefix from a key the wrapped store returned.</summary>
    /// <exception cref="InvalidOperationException">The key lies outside the tenant's prefix.</exception>
    public string Strip(string key)
    {
        // Every key a scoped call can reach starts with the prefix; one that does not is another tenant's blob or a
        // host blob, and returning it would leak it across the tenant boundary.
        if (!key.StartsWith(KeyPrefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The blob store returned a key outside the ambient tenant's scope; refusing to return it."
            );
        }

        return key[KeyPrefix.Length..];
    }

    public BlobInfo Strip(BlobInfo info)
    {
        return new BlobInfo
        {
            BlobKey = Strip(info.BlobKey),
            Created = info.Created,
            Modified = info.Modified,
            Size = info.Size,
            Metadata = info.Metadata,
        };
    }

    public BlobBulkResult Strip(BlobBulkResult result, string container)
    {
        var path = Strip(result.Path);

        return result.Location is null
            ? new BlobBulkResult(container, path, result.Result)
            : new BlobBulkResult(new BlobLocation(container, path), result.Result);
    }
}
