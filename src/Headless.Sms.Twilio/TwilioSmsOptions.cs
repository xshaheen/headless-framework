// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Sms.Twilio;

public sealed class TwilioSmsOptions
{
    public required string Sid { get; init; }

    public required string AuthToken { get; init; }

    public required string PhoneNumber { get; init; }

    public decimal? MaxPrice { get; init; }

    public string? Region { get; init; }

    public string? Edge { get; init; }
}

[UsedImplicitly]
internal sealed class TwilioSmsOptionsValidator : AbstractValidator<TwilioSmsOptions>
{
    public TwilioSmsOptionsValidator()
    {
        RuleFor(x => x.Sid).NotEmpty();
        RuleFor(x => x.AuthToken).NotEmpty();
        RuleFor(x => x.PhoneNumber).NotEmpty().InternationalPhoneNumber();
        RuleFor(x => x.MaxPrice).GreaterThanOrEqualTo(0);
    }
}
