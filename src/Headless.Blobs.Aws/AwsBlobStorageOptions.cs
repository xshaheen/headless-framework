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
}

internal sealed class AwsBlobStorageOptionsValidator : AbstractValidator<AwsBlobStorageOptions>
{
    public AwsBlobStorageOptionsValidator()
    {
        RuleFor(x => x.MaxBulkParallelism).GreaterThan(0);
    }
}
