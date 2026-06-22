// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.S3;
using FluentValidation;

namespace Headless.Blobs.Aws;

/// <summary>Configuration for the AWS S3 blob storage provider.</summary>
public sealed class AwsBlobStorageOptions
{
    /// <summary>
    /// When <see langword="true"/> (the default), uploads use chunked Transfer-Encoding, which allows streaming
    /// without a known <c>Content-Length</c>. Set to <see langword="false"/> when the S3-compatible endpoint
    /// rejects chunked encoding (for example Cloudflare R2).
    /// </summary>
    public bool UseChunkEncoding { get; set; } = true;

    /// <summary>
    /// When <see langword="true"/>, AWS Signature Version 4 payload signing is disabled for uploads. Required for
    /// endpoints that reject signed payloads (for example Cloudflare R2). Default is <see langword="false"/>.
    /// </summary>
    public bool DisablePayloadSigning { get; set; }

    /// <summary>
    /// Canned ACL applied to every uploaded object. Defaults to <see cref="S3CannedACL.Private"/>. Set to
    /// <see langword="null"/> for backends without ACL support (for example Cloudflare R2).
    /// </summary>
    public S3CannedACL? CannedAcl { get; set; } = S3CannedACL.Private;

    /// <summary>Maximum degree of parallelism for bulk operations. Default is 10.</summary>
    public int MaxBulkParallelism { get; set; } = 10;

    /// <summary>
    /// When <see langword="true"/> (the default), uploads and copies create the target bucket if it does not
    /// already exist. The check/create runs at most once per bucket per storage instance. Set to
    /// <see langword="false"/> for backends whose credentials cannot create buckets (for example Cloudflare R2
    /// object-scoped tokens); a missing bucket then surfaces as an error from the write operation. Explicit
    /// <see cref="IBlobStorage.CreateContainerAsync"/> calls ensure the container regardless of this setting
    /// (the result is cached per instance, so the bucket is not re-probed once ensured).
    /// </summary>
    public bool AutoCreateContainer { get; set; } = true;
}

internal sealed class AwsBlobStorageOptionsValidator : AbstractValidator<AwsBlobStorageOptions>
{
    public AwsBlobStorageOptionsValidator()
    {
        RuleFor(x => x.MaxBulkParallelism).GreaterThan(0);
    }
}
