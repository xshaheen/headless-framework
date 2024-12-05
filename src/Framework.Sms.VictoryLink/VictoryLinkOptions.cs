// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Framework.FluentValidation;

namespace Framework.Sms.VictoryLink;

public sealed class VictoryLinkOptions
{
    public required string SendSmsEndpointUrl { get; init; } = "https://smsvas.vlserv.com/VLSMSPlatformResellerAPI/NewSendingAPI/api/SMSSender/SendSMS";

    public required string Sender { get; init; }

    public required string UserName { get; init; }

    public required string Password { get; init; }
}

internal sealed class VictoryLinkOptionsValidator : AbstractValidator<VictoryLinkOptions>
{
    public VictoryLinkOptionsValidator()
    {
        RuleFor(x => x.SendSmsEndpointUrl).NotEmpty().HttpUrl();
        RuleFor(x => x.Sender).NotEmpty();
        RuleFor(x => x.UserName).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
    }
}
