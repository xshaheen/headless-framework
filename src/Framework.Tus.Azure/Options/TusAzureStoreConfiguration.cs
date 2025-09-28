// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs.Models;
using FluentValidation;

namespace Framework.Tus.Options;

public sealed class TusAzureStoreOptions
{
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

    public int BlobMaxChunkSize { get; set; } = 100 * 1024 * 1024; // 100MB

    public int BlobDefaultChunkSize { get; set; } = 4 * 1024 * 1024; // 4MB

    /// <summary>
    /// The lease duration must be between 15 and 60 seconds, or infinite (-1).
    /// Default is -1.
    /// </summary>
    public TimeSpan LeaseDuration { get; set; } = Timeout.InfiniteTimeSpan;

    /// <summary>Access type when creating a new container if it does not exist.</summary>
    public PublicAccessType ContainerPublicAccessType { get; set; } = PublicAccessType.None;
}

public class TusAzureStoreOptionsValidator : AbstractValidator<TusAzureStoreOptions>
{
    public TusAzureStoreOptionsValidator()
    {
        RuleFor(x => x.ContainerName).NotEmpty();

        RuleFor(x => x.BlobMaxChunkSize)
            .InclusiveBetween(1, 100 * 1024 * 1024) // 100MB
            .WithMessage("BlockBlobMaxChunkSize must be between 1 byte and 100MB");

        RuleFor(x => x.LeaseDuration)
            .InclusiveBetween(TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(60)) // 15 seconds to 60 minutes
            .When(x => x.LeaseDuration != Timeout.InfiniteTimeSpan)
            .WithMessage("DefaultLeaseTime must be between 15 seconds and 60 minutes or infinite (-1)");

        RuleFor(x => x.ContainerPublicAccessType).IsInEnum();
    }
}
