using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Framework.Blobs.Azure;

public sealed class AzureStorageOptions
{
    public required string AccountName { get; init; }

    public required string AccountKey { get; init; }

    public ILoggerFactory? LoggerFactory { get; init; }
}

public sealed class AzureStorageOptionsValidator : AbstractValidator<AzureStorageOptions>
{
    public AzureStorageOptionsValidator()
    {
        RuleFor(x => x.AccountName).NotEmpty().MinimumLength(2);
        RuleFor(x => x.AccountKey).NotEmpty().MinimumLength(2);
    }
}
