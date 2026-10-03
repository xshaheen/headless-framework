// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.RegularExpressions;
using Headless.Blobs.Internal;
using Headless.Checks;
using Headless.MultiTenancy;
using Headless.Primitives;

namespace Headless.Blobs;

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
