using FluentValidation;

namespace Framework.Blobs.Azure;

public sealed class AzureStorageOptions
{
    public required string AccountName { get; set; }

    public required string AccountKey { get; set; }
}

public sealed class AzureStorageOptionsValidator : AbstractValidator<AzureStorageOptions>
{
    public AzureStorageOptionsValidator()
    {
        RuleFor(x => x.AccountName).NotEmpty().MinimumLength(2);
        RuleFor(x => x.AccountKey).NotEmpty().MinimumLength(2);
    }
}
