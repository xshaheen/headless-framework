// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Serializer;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Headless.Blobs.Redis;

public sealed class RedisBlobStorageOptions
{
    public required IConnectionMultiplexer ConnectionMultiplexer { get; set; }

    public ILoggerFactory? LoggerFactory { get; set; }

    public ISerializer? Serializer { get; set; }

    /// <summary>Maximum degree of parallelism for bulk operations. Default is 10.</summary>
    public int MaxBulkParallelism { get; set; } = 10;

    /// <summary>Maximum blob size in bytes. Default is 10 MB. Set to 0 to disable.</summary>
    public long MaxBlobSizeBytes { get; set; } = 10 * 1024 * 1024;
}

internal sealed class RedisBlobStorageOptionsValidator : AbstractValidator<RedisBlobStorageOptions>
{
    public RedisBlobStorageOptionsValidator()
    {
        RuleFor(x => x.ConnectionMultiplexer).NotNull();
        RuleFor(x => x.MaxBulkParallelism).GreaterThan(0);
        RuleFor(x => x.MaxBlobSizeBytes).GreaterThanOrEqualTo(0);
    }
}
