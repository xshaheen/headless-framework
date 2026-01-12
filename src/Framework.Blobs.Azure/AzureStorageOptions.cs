// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs.Models;
using FluentValidation;

namespace Framework.Blobs.Azure;

public sealed class AzureStorageOptions
{
    /// <summary>Whether to create the container if it does not already exist.</summary>
    public bool CreateContainerIfNotExists { get; set; } = true;

    /// <summary>Access type when creating a new container if it does not exist.</summary>
    public PublicAccessType ContainerPublicAccessType { get; set; } = PublicAccessType.None;

    /// <summary>Maximum degree of parallelism for bulk upload operations. Default is 10.</summary>
    public int MaxBulkParallelism { get; set; } = 10;

    /// <summary>Cache-Control header for uploaded blobs. Default is 90 days (max-age=7776000, must-revalidate).</summary>
    public string CacheControl { get; set; } = "max-age=7776000, must-revalidate";
}

public sealed class AzureStorageOptionsValidator : AbstractValidator<AzureStorageOptions>
{
    public AzureStorageOptionsValidator()
    {
        RuleFor(x => x.ContainerPublicAccessType).IsInEnum();
        RuleFor(x => x.MaxBulkParallelism).GreaterThan(0);
    }
}
