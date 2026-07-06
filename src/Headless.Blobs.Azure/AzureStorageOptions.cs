// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs.Models;
using FluentValidation;

namespace Headless.Blobs.Azure;

/// <summary>Configuration for the Azure Blob Storage provider.</summary>
public sealed class AzureStorageOptions
{
    /// <summary>
    /// Access type applied when <see cref="IBlobContainerManager.EnsureContainerAsync"/> creates a new container.
    /// </summary>
    /// <remarks>
    /// This is a deliberate full-fidelity pass-through of the Azure SDK type <see cref="PublicAccessType"/>: the
    /// whole container-access vocabulary is exposed verbatim so no Azure option is lost behind a lossy Headless
    /// wrapper. It intentionally couples this option to <c>Azure.Storage.Blobs</c>.
    /// </remarks>
    public PublicAccessType ContainerPublicAccessType { get; set; } = PublicAccessType.None;

    /// <summary>Maximum degree of parallelism for bulk upload operations. Default is 10.</summary>
    public int MaxBulkParallelism { get; set; } = 10;

    /// <summary>Cache-Control header for uploaded blobs. Default is 90 days (max-age=7776000, must-revalidate).</summary>
    public string CacheControl { get; set; } = "max-age=7776000, must-revalidate";
}

internal sealed class AzureStorageOptionsValidator : AbstractValidator<AzureStorageOptions>
{
    public AzureStorageOptionsValidator()
    {
        RuleFor(x => x.ContainerPublicAccessType).IsInEnum();
        RuleFor(x => x.MaxBulkParallelism).GreaterThan(0);
    }
}
