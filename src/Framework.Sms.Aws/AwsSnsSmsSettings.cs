using FluentValidation;

namespace Framework.Sms.Aws;

public sealed class AwsSnsSmsSettings
{
    public required string SenderId { get; init; }
}

internal sealed class AwsSnsSmsSettingsValidator : AbstractValidator<AwsSnsSmsSettings>
{
    public AwsSnsSmsSettingsValidator()
    {
        RuleFor(x => x.SenderId).NotEmpty();
    }
}
