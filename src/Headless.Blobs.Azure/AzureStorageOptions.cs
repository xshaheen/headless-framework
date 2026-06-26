// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs.Models;
using FluentValidation;

namespace Headless.Blobs.Azure;

/// <summary>Configuration for the Azure Blob Storage provider.</summary>
public sealed class AzureStorageOptions
{
    /// <summary>
    /// Legacy flag retained for provider option-shape compatibility. It is no longer consulted by the data-plane
    /// write path: <see cref="IBlobStorage"/> never auto-creates a missing container — a missing container surfaces
    /// as an error. Container lifecycle now lives on the separately-registered <see cref="IBlobContainerManager"/>
    /// capability (<c>EnsureContainerAsync</c> always creates regardless of this flag, caching the result per
    /// instance).
    /// </summary>
    public bool AutoCreateContainer { get; set; } = true;

    /// <summary>
    /// Access type applied when <see cref="IBlobContainerManager.EnsureContainerAsync"/> creates a new container.
    /// </summary>
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
