// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.S3;
using FluentValidation;

namespace Headless.Blobs.Aws;

public sealed class AwsBlobStorageOptions
{
    public bool UseChunkEncoding { get; set; } = true;

    public bool DisablePayloadSigning { get; set; }

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
