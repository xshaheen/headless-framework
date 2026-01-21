// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Framework.Sms.VictoryLink;

public sealed class VictoryLinkSmsOptions
{
    public required string Endpoint { get; init; } =
        "https://smsvas.vlserv.com/VLSMSPlatformResellerAPI/NewSendingAPI/api/SMSSender/SendSMS";

    public required string Sender { get; init; }

    public required string UserName { get; init; }

    public required string Password { get; init; }
}

internal sealed class VictoryLinkSmsOptionsValidator : AbstractValidator<VictoryLinkSmsOptions>
{
    public VictoryLinkSmsOptionsValidator()
    {
        RuleFor(x => x.Endpoint).NotEmpty().HttpUrl();
        RuleFor(x => x.Sender).NotEmpty();
        RuleFor(x => x.UserName).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
    }
}
