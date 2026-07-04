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
/// Consumers feature-detect the capability with an <c>is</c>-cast from the resolved <see cref="IBlobStorage"/>
/// instead of relying on a runtime failure:
/// <code>
/// if (storage is IPresignedUrlBlobStorage presigned)
/// {
///     var location = new BlobLocation("images", "avatars/user-42.png");
///     var url = await presigned.GetPresignedDownloadUrlAsync(location, TimeSpan.FromMinutes(15));
/// }
/// </code>
/// The <c>is</c>-cast stays honest here because both AWS and Cloudflare R2 (which reuses the AWS storage type)
/// support signing — unlike <see cref="IBlobContainerManager"/>, which must distinguish providers that share a
/// storage implementation and is therefore resolved from DI rather than cast from the storage instance.
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
    /// <param name="location">The blob the URL grants read access to.</param>
    /// <param name="expiry">How long the URL remains valid, measured from now.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A signed, time-limited URL for downloading the blob.</returns>
    ValueTask<Uri> GetPresignedDownloadUrlAsync(
        BlobLocation location,
        TimeSpan expiry,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Creates a pre-authenticated URL that allows uploading (HTTP PUT) to the specified blob key until it expires.
    /// </summary>
    /// <param name="location">The blob the URL grants write access to.</param>
    /// <param name="expiry">How long the URL remains valid, measured from now.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A signed, time-limited URL for uploading the blob.</returns>
    ValueTask<Uri> GetPresignedUploadUrlAsync(
        BlobLocation location,
        TimeSpan expiry,
        CancellationToken cancellationToken = default
    );
}
