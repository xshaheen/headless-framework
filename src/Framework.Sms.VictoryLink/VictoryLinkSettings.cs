using FluentValidation;

namespace Framework.Sms.VictoryLink;

public sealed class VictoryLinkSettings
{
    public required string Sender { get; init; }

    public required string UserName { get; init; }

    public required string Password { get; init; }
}

[UsedImplicitly]
internal sealed class VictoryLinkSettingsValidator : AbstractValidator<VictoryLinkSettings>
{
    public VictoryLinkSettingsValidator()
    {
        RuleFor(x => x.Sender).NotEmpty();
        RuleFor(x => x.UserName).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
    }
}
