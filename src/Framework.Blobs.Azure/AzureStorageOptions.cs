// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Storage.Blobs.Models;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Framework.Blobs.Azure;

public sealed class AzureStorageOptions
{
    public ILoggerFactory? LoggerFactory { get; set; }

    /// <summary>Whether to create the container if it does not already exist.</summary>
    public bool CreateContainerIfNotExists { get; set; } = true;

    /// <summary>Access type when creating a new container if it does not exist.</summary>
    public PublicAccessType ContainerPublicAccessType { get; set; } = PublicAccessType.None;
}

public sealed class AzureStorageOptionsValidator : AbstractValidator<AzureStorageOptions>
{
    public AzureStorageOptionsValidator()
    {
        RuleFor(x => x.ContainerPublicAccessType).IsInEnum();
    }
}
