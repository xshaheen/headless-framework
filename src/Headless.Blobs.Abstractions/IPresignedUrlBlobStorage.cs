// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Blobs;

/// <summary>
/// Optional capability for blob backends that can mint time-limited, pre-authenticated URLs granting direct
/// access to a private blob without proxying the bytes through the application.
/// </summary>
/// <remarks>
/// <para>
/// This is a capability interface, not part of <see cref="IBlobStorage"/>: only backends with a native signing
/// mechanism implement it (S3 SigV4 for AWS and Cloudflare R2, SAS for Azure). Backends with no URL concept
/// (file system, Redis, SSH) deliberately do not implement it.
/// </para>
/// <para>
/// Consumers feature-detect the capability instead of relying on a runtime failure:
/// <code>
/// if (storage is IPresignedUrlBlobStorage presigned)
/// {
///     var url = await presigned.GetPresignedDownloadUrlAsync(container, blobName, TimeSpan.FromMinutes(15));
/// }
/// </code>
/// </para>
/// <para>
/// Implementing the interface advertises that the backend <i>can</i> sign URLs; it does not guarantee every
/// configuration can. A backend whose client was wired without signing credentials may throw
/// <see cref="InvalidOperationException"/> at call time.
/// </para>
/// </remarks>
[PublicAPI]
public interface IPresignedUrlBlobStorage
{
    /// <summary>
    /// Creates a pre-authenticated URL that allows downloading (HTTP GET) the specified blob until it expires.
    /// </summary>
    /// <param name="container">Container path segments.</param>
    /// <param name="blobName">Name of the blob the URL grants access to.</param>
    /// <param name="expiry">How long the URL remains valid, measured from now.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A signed, time-limited URL for downloading the blob.</returns>
    ValueTask<Uri> GetPresignedDownloadUrlAsync(
        string[] container,
        string blobName,
        TimeSpan expiry,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Creates a pre-authenticated URL that allows uploading (HTTP PUT) to the specified blob key until it expires.
    /// </summary>
    /// <param name="container">Container path segments.</param>
    /// <param name="blobName">Name of the blob the URL grants write access to.</param>
    /// <param name="expiry">How long the URL remains valid, measured from now.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A signed, time-limited URL for uploading the blob.</returns>
    ValueTask<Uri> GetPresignedUploadUrlAsync(
        string[] container,
        string blobName,
        TimeSpan expiry,
        CancellationToken cancellationToken = default
    );
}
