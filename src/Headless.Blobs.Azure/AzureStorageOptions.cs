// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs.Models;
using FluentValidation;

namespace Headless.Blobs.Azure;

/// <summary>Configuration for the Azure Blob Storage provider.</summary>
public sealed class AzureStorageOptions
{
    /// <summary>
    /// When <see langword="true"/> (the default), uploads and copies create the target container if it does not
    /// already exist. The check/create runs at most once per container per storage instance. Set to
    /// <see langword="false"/> for clients whose credentials cannot create containers; a missing container then
    /// surfaces as an error from the operation. Explicit <see cref="IBlobStorage.CreateContainerAsync"/> calls
    /// ensure the container regardless of this setting (the result is cached per instance).
    /// </summary>
    public bool AutoCreateContainer { get; set; } = true;

    /// <summary>Access type when creating a new container if it does not exist.</summary>
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
