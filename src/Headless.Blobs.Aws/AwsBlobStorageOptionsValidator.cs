// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Blobs.Aws;

internal sealed class AwsBlobStorageOptionsValidator : AbstractValidator<AwsBlobStorageOptions>
{
    public AwsBlobStorageOptionsValidator()
    {
        RuleFor(x => x.MaxBulkParallelism).GreaterThan(0);
    }
}
