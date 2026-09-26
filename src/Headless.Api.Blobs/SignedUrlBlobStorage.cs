// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Headers;
using Headless.Checks;

namespace Headless.Blobs;

/// <summary>
/// Adds <see cref="IPresignedUrlBlobStorage"/> to a store without native presign by minting URLs that point at the
/// signed-URL endpoint; every <see cref="IBlobStorage"/> call passes through to the wrapped store.
/// </summary>
/// <remarks>
/// It wraps the store rather than registering a separate service so that the documented capability check,
/// <c>storage is IPresignedUrlBlobStorage</c>, answers the same way for every provider.
/// </remarks>
internal sealed class SignedUrlBlobStorage(IBlobStorage inner, string? store, BlobSignedUrlSigner signer)
    : IBlobStorage,
        IPresignedUrlBlobStorage
{
    private bool _disposed;

    public bool RequiresContainerProvisioning => inner.RequiresContainerProvisioning;

    #region Presigned Urls

    public ValueTask<Uri> GetPresignedDownloadUrlAsync(
        BlobLocation location,
        TimeSpan expiry,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        Argument.IsPositive(expiry);

        return ValueTask.FromResult(
            signer.CreateUrl(BlobSignedUrlAccess.Download, store, location, expiry, constraints: null)
        );
    }

    public ValueTask<Uri> GetPresignedUploadUrlAsync(
        BlobLocation location,
        TimeSpan expiry,
        PresignedUploadConstraints? constraints = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        Argument.IsPositive(expiry);

        if (constraints?.MaxLength is { } maxLength)
        {
            Argument.IsPositive(maxLength, paramName: nameof(constraints));
        }

        if (constraints?.ContentType is { } contentType && !MediaTypeHeaderValue.TryParse(contentType, out _))
        {
            throw new ArgumentException($"'{contentType}' is not a valid media type.", nameof(constraints));
        }

        return ValueTask.FromResult(signer.CreateUrl(BlobSignedUrlAccess.Upload, store, location, expiry, constraints));
    }

    #endregion

    #region Pass-through

    public ValueTask UploadAsync(
        BlobLocation location,
        Stream content,
        IReadOnlyDictionary<string, string>? metadata = null,
        string? contentType = null,
        CancellationToken cancellationToken = default
    )
    {
        return inner.UploadAsync(location, content, metadata, contentType, cancellationToken);
    }

    public ValueTask<IReadOnlyList<BlobBulkResult>> BulkUploadAsync(
        string container,
        IReadOnlyCollection<BlobUploadRequest> blobs,
        CancellationToken cancellationToken = default
    )
    {
        return inner.BulkUploadAsync(container, blobs, cancellationToken);
    }

    public ValueTask<bool> DeleteAsync(BlobLocation location, CancellationToken cancellationToken = default)
    {
        return inner.DeleteAsync(location, cancellationToken);
    }

    public ValueTask<IReadOnlyList<BlobBulkResult>> BulkDeleteAsync(
        string container,
        IReadOnlyCollection<string> paths,
        CancellationToken cancellationToken = default
    )
    {
        return inner.BulkDeleteAsync(container, paths, cancellationToken);
    }

    public ValueTask<int> DeleteAllAsync(BlobQuery query, CancellationToken cancellationToken = default)
    {
        return inner.DeleteAllAsync(query, cancellationToken);
    }

    public ValueTask<bool> MoveAsync(
        BlobLocation source,
        BlobLocation destination,
        CancellationToken cancellationToken = default
    )
    {
        return inner.MoveAsync(source, destination, cancellationToken);
    }

    public ValueTask<bool> CopyAsync(
        BlobLocation source,
        BlobLocation destination,
        CancellationToken cancellationToken = default
    )
    {
        return inner.CopyAsync(source, destination, cancellationToken);
    }

    public ValueTask<bool> ExistsAsync(BlobLocation location, CancellationToken cancellationToken = default)
    {
        return inner.ExistsAsync(location, cancellationToken);
    }

    [MustDisposeResource]
    public ValueTask<BlobDownloadResult?> OpenReadStreamAsync(
        BlobLocation location,
        CancellationToken cancellationToken = default
    )
    {
        return inner.OpenReadStreamAsync(location, cancellationToken);
    }

    public ValueTask<BlobInfo?> GetBlobInfoAsync(BlobLocation location, CancellationToken cancellationToken = default)
    {
        return inner.GetBlobInfoAsync(location, cancellationToken);
    }

    public ValueTask<BlobPage> ListAsync(BlobQuery query, CancellationToken cancellationToken = default)
    {
        return inner.ListAsync(query, cancellationToken);
    }

    #endregion

    public ValueTask DisposeAsync()
    {
        // A named store is also resolved through its keyed IPresignedUrlBlobStorage forward, so the container tracks
        // this instance twice and disposes it twice. The wrapped store was built by the decorating factory, not the
        // container, so this instance owns releasing it exactly once.
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;

        return inner.DisposeAsync();
    }
}
