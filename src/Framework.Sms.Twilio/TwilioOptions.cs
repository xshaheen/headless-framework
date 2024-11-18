// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Framework.FluentValidation;

namespace Framework.Sms.Twilio;

public sealed class TwilioOptions
{
    public required string Sid { get; init; }

    public required string AuthToken { get; init; }

    public required string PhoneNumber { get; init; }
}

internal sealed class TwilioOptionsValidator : AbstractValidator<TwilioOptions>
{
    public TwilioOptionsValidator()
    {
        RuleFor(x => x.Sid).NotEmpty();
        RuleFor(x => x.AuthToken).NotEmpty();
        RuleFor(x => x.PhoneNumber).NotEmpty().InternationalPhoneNumber();
    }
}
