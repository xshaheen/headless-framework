// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using FluentValidation;
using Framework.FluentValidation;

namespace Framework.Tus.Options;

public sealed class TusAzureStoreOptions
{
    /// <summary>The connection string to the Azure Blob Storage account.</summary>
    public required string ConnectionString { get; set; }

    /// <summary>The name of the Azure Blob Storage container to use for storing uploads.</summary>
    public string ContainerName { get; set; } = "tus-uploads";

    /// <summary>A prefix to add to all blob names within the container.</summary>
    public string BlobPrefix { get; set; } = "uploads/";

    /// <summary>Whether to create the container if it does not already exist.</summary>
    public bool CreateContainerIfNotExists { get; set; } = true;

    /// <summary>
    /// Whether to enable chunk splitting for large uploads. When enabled, large chunks will be split into smaller chunks
    /// that comply with Azure Blob Storage limits. This is useful for clients that may send large chunks.
    /// </summary>
    public bool EnableChunkSplitting { get; set; } = true;

    // Block Blob limits
    public long BlockBlobSizeLimit { get; set; } = 4750L * 1024 * 1024 * 1024; // 4.75TB  (Azure limit)

    public int BlobMaxChunkSize { get; set; } = 100 * 1024 * 1024; // 100MB

    public int BlobDefaultChunkSize { get; set; } = 4 * 1024 * 1024; // 4MB

    public int MaxChunkSplitSize { get; set; } = 32 * 1024 * 1024; // 32MB

    // Locking configuration
    public TimeSpan DefaultLeaseTime { get; set; } = TimeSpan.FromMinutes(1);

    public TimeSpan LeaseRenewalInterval { get; set; } = TimeSpan.FromSeconds(30);

    // Performance tuning
    public int MaxConcurrentUploads { get; set; } = 50;

    public TimeSpan BlockListCacheTime { get; set; } = TimeSpan.FromMinutes(5);

    // TODO: Use/optimize these settings

    /// <summary>Commit every N blocks for large uploads.</summary>
    public int CommitBatchSize { get; set; } = 10;

    public SpecializedBlobClientOptions BlobClientOptions { get; set; } =
        new()
        {
            Retry =
            {
                MaxRetries = 3,
                Mode = RetryMode.Exponential,
                Delay = TimeSpan.FromSeconds(0.4),
                NetworkTimeout = TimeSpan.FromSeconds(10),
                MaxDelay = TimeSpan.FromMinutes(1),
            },
        };
}

public class TusAzureStoreOptionsValidator : AbstractValidator<TusAzureStoreOptions>
{
    public TusAzureStoreOptionsValidator()
    {
        RuleFor(x => x.ConnectionString).NotEmpty();

        RuleFor(x => x.ContainerName).NotEmpty();

        RuleFor(x => x.BlobMaxChunkSize)
            .InclusiveBetween(1, 100 * 1024 * 1024) // 100MB
            .WithMessage("BlockBlobMaxChunkSize must be between 1 byte and 100MB");

        RuleFor(x => x.DefaultLeaseTime)
            .InclusiveBetween(TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(60)) // 15 seconds to 60 minutes
            .WithMessage("DefaultLeaseTime must be between 15 seconds and 60 minutes");

        RuleFor(x => x.CommitBatchSize).GreaterThan(0);
        RuleFor(x => x.BlobClientOptions).NotNull();
    }
}
