// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Framework.FluentValidation;

namespace Framework.Sms.Infobip;

public sealed class InfobipSmsOptions
{
    public required string Sender { get; set; }

    public required string ApiKey { get; set; }

    public required string BasePath { get; set; }
}

internal sealed class InfobipSmsOptionsValidator : AbstractValidator<InfobipSmsOptions>
{
    public InfobipSmsOptionsValidator()
    {
        RuleFor(x => x.Sender).NotEmpty();
        RuleFor(x => x.ApiKey).NotEmpty();
        RuleFor(x => x.BasePath).NotEmpty().HttpUrl();
    }
}
