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
}

internal sealed class AwsBlobStorageOptionsValidator : AbstractValidator<AwsBlobStorageOptions>
{
    public AwsBlobStorageOptionsValidator()
    {
        RuleFor(x => x.MaxBulkParallelism).GreaterThan(0);
    }
}
