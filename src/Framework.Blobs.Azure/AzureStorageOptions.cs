// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Framework.FluentValidation;
using Microsoft.Extensions.Logging;

namespace Framework.Blobs.Azure;

public sealed class AzureStorageOptions
{
    public required string AccountName { get; set; }

    public required string AccountKey { get; set; }

    /// <summary>
    /// The URL of the Azure Storage Account.
    /// It should be in the format of `http://AccountName.blob.core.windows.net`.
    /// </summary>
    public required string AccountUrl { get; set; }

    public ILoggerFactory? LoggerFactory { get; set; }
}

public sealed class AzureStorageOptionsValidator : AbstractValidator<AzureStorageOptions>
{
    public AzureStorageOptionsValidator()
    {
        RuleFor(x => x.AccountName).NotEmpty().MinimumLength(2);
        RuleFor(x => x.AccountKey).NotEmpty().MinimumLength(2);
        RuleFor(x => x.AccountUrl).NotEmpty().HttpUrl();
    }
}
