// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.DataProtection;

/// <summary>Options for <c>PersistKeysToBlobStorage</c>: where the data-protection key ring is stored.</summary>
[PublicAPI]
public sealed class BlobStorageDataProtectionOptions
{
    /// <summary>The container used when <see cref="ContainerName"/> is not set.</summary>
    public const string DefaultContainerName = "DataProtection";

    /// <summary>
    /// The top-level blob container that holds the key XML blobs. Defaults to <c>DataProtection</c>.
    /// </summary>
    /// <remarks>
    /// The provider's naming normalizer applies, so on S3 the default becomes the bucket <c>dataprotection</c>. S3
    /// bucket names are global across every AWS account, so set a name unique to the application and environment
    /// there. Set a distinct name too when several applications share one Azure storage account, file-system root,
    /// or Redis server: every application reading one container loads every key in it.
    /// </remarks>
    public string ContainerName { get; set; } = DefaultContainerName;
}

internal sealed class BlobStorageDataProtectionOptionsValidator : AbstractValidator<BlobStorageDataProtectionOptions>
{
    public BlobStorageDataProtectionOptionsValidator()
    {
        RuleFor(x => x.ContainerName).NotEmpty();
    }
}
