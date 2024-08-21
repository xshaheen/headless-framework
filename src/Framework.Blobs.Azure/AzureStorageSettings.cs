using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Framework.Blobs.Azure;

public sealed class AzureStorageSettings
{
    public required string AccountName { get; init; }

    public required string AccountKey { get; init; }

    public ILoggerFactory? LoggerFactory { get; init; }
}

public sealed class AzureStorageSettingsValidator : AbstractValidator<AzureStorageSettings>
{
    public AzureStorageSettingsValidator()
    {
        RuleFor(x => x.AccountName).NotEmpty().MinimumLength(2);
        RuleFor(x => x.AccountKey).NotEmpty().MinimumLength(2);
    }
}
