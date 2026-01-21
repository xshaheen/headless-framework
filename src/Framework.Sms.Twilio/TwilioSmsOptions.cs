// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Framework.Sms.Twilio;

public sealed class TwilioSmsOptions
{
    public required string Sid { get; init; }

    public required string AuthToken { get; init; }

    public required string PhoneNumber { get; init; }

    public decimal? MaxPrice { get; set; }

    public string? Region { get; set; }

    public string? Edge { get; set; }
}

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
