// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Blobs;

/// <summary>
/// Restrictions a presigned upload URL imposes on the request that uses it, passed to
/// <see cref="IPresignedUrlBlobStorage.GetPresignedUploadUrlAsync"/>.
/// </summary>
/// <remarks>
/// A backend enforces each constraint it can and throws <see cref="NotSupportedException"/> for one it cannot, so a
/// constraint is never silently dropped. S3 and Cloudflare R2 sign <see cref="ContentType"/> into the URL but cannot
/// bound the size of a presigned PUT; Azure SAS supports neither; the signed-URL endpoint in
/// <c>Headless.Blobs.SignedUrlEndpoint</c> enforces both.
/// </remarks>
[PublicAPI]
public sealed record PresignedUploadConstraints
{
    /// <summary>
    /// The media type the upload must declare in its <c>Content-Type</c> header, or <see langword="null"/> to accept
    /// any type.
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// The largest body, in bytes, the upload may send, or <see langword="null"/> for no per-URL limit.
    /// </summary>
    public long? MaxLength { get; init; }
}
