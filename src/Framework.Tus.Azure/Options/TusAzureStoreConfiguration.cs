// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Framework.Tus.Options;

public class TusAzureStoreOptions
{
    public required string ConnectionString { get; set; }

    public string ContainerName { get; set; } = "tus-uploads";

    public string BlobPrefix { get; set; } = "uploads/";

    public bool CreateContainerIfNotExists { get; set; } = true;

    public AzureBlobStrategy BlobStrategy { get; set; } = AzureBlobStrategy.Auto;

    // Append Blob limits (Go tusd compatible)
    public long AppendBlobSizeLimit { get; set; } = 195L * 1024 * 1024 * 1024; // 195GB

    public int AppendBlobMaxChunkSize { get; set; } = 4 * 1024 * 1024; // 4MB

    // Block Blob limits
    public long BlockBlobSizeLimit { get; set; } = 4750L * 1024 * 1024 * 1024; // 4.75TB

    public int BlockBlobMaxChunkSize { get; set; } = 100 * 1024 * 1024; // 100MB

    public int BlockBlobDefaultChunkSize { get; set; } = 4 * 1024 * 1024; // 4MB

    // Chunk splitting configuration
    public bool EnableChunkSplitting { get; set; } = true;

    public int MaxChunkSplitSize { get; set; } = 32 * 1024 * 1024; // 32MB

    // Visibility configuration (Go tusd compatible)
    public bool HideBlobUntilComplete { get; set; } = false;

    // Locking configuration
    public TimeSpan DefaultLeaseTime { get; set; } = TimeSpan.FromMinutes(1);

    public TimeSpan LeaseRenewalInterval { get; set; } = TimeSpan.FromSeconds(30);

    // Performance tuning
    public int MaxConcurrentUploads { get; set; } = 50;

    public TimeSpan BlockListCacheTime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Commit every N blocks for large uploads.</summary>
    public int CommitBatchSize { get; set; } = 10;
}

public class TusAzureStoreOptionsValidator : AbstractValidator<TusAzureStoreOptions>
{
    public TusAzureStoreOptionsValidator()
    {
        RuleFor(x => x.ConnectionString).NotEmpty();

        RuleFor(x => x.ContainerName).NotEmpty();

        RuleFor(x => x.AppendBlobMaxChunkSize)
            .InclusiveBetween(1, 4 * 1024 * 1024) // 4MB
            .WithMessage("AppendBlobMaxChunkSize must be between 1 byte and 4MB");

        RuleFor(x => x.BlockBlobMaxChunkSize)
            .InclusiveBetween(1, 100 * 1024 * 1024) // 100MB
            .WithMessage("BlockBlobMaxChunkSize must be between 1 byte and 100MB");

        RuleFor(x => x.DefaultLeaseTime)
            .InclusiveBetween(TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(60)) // 15 seconds to 60 minutes
            .WithMessage("DefaultLeaseTime must be between 15 seconds and 60 minutes");

        RuleFor(x => x.CommitBatchSize).GreaterThan(0);
    }
}
