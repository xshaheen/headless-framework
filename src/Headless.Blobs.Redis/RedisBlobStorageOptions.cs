// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Serializer;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Headless.Blobs.Redis;

/// <summary>
/// Configuration for the Redis blob storage provider.
/// </summary>
/// <remarks>
/// Redis blob storage is designed for small or ephemeral blobs. Large blobs will approach or exceed Redis
/// memory limits; use <see cref="MaxBlobSizeBytes"/> to enforce an upper bound. For durable, large-file
/// storage, prefer the AWS S3, Azure, or file-system providers instead.
/// </remarks>
[PublicAPI]
public sealed class RedisBlobStorageOptions
{
    /// <summary>The Redis connection multiplexer to use. Required.</summary>
    public required IConnectionMultiplexer ConnectionMultiplexer { get; set; }

    /// <summary>
    /// Optional logger factory for Redis-level diagnostics. When <see langword="null"/>, a no-op logger is used.
    /// </summary>
    public ILoggerFactory? LoggerFactory { get; set; }

    /// <summary>
    /// Optional custom serializer for blob metadata. Defaults to the framework's registered <c>IJsonSerializer</c>
    /// when <see langword="null"/>.
    /// </summary>
    public ISerializer? Serializer { get; set; }

    /// <summary>Maximum degree of parallelism for bulk operations. Default is 10.</summary>
    public int MaxBulkParallelism { get; set; } = 10;

    /// <summary>
    /// Maximum allowed size for a single blob upload, in bytes. Default is 10 MB (10 × 1 024 × 1 024).
    /// Set to <c>0</c> to disable size enforcement (not recommended for production).
    /// </summary>
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
