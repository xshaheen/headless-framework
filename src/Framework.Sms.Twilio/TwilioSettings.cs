using FluentValidation;
using Framework.FluentValidation;

namespace Framework.Sms.Twilio;

public sealed class TwilioSettings
{
    public required string Sid { get; init; }

    public required string AuthToken { get; init; }

    public required string PhoneNumber { get; init; }
}

[UsedImplicitly]
internal sealed class TwilioSettingsValidator : AbstractValidator<TwilioSettings>
{
    public TwilioSettingsValidator()
    {
        RuleFor(x => x.Sid).NotEmpty();
        RuleFor(x => x.AuthToken).NotEmpty();
        RuleFor(x => x.PhoneNumber).NotEmpty().InternationalPhoneNumber();
    }
}
