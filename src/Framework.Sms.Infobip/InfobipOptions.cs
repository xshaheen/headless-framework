// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Framework.FluentValidation;

namespace Framework.Sms.Infobip;

public sealed class InfobipOptions
{
    public required string Sender { get; set; }

    public required string ApiKey { get; set; }

    public required string BasePath { get; set; }
}

internal sealed class InfobipOptionsValidator : AbstractValidator<InfobipOptions>
{
    public InfobipOptionsValidator()
    {
        RuleFor(x => x.Sender).NotEmpty();
        RuleFor(x => x.ApiKey).NotEmpty();
        RuleFor(x => x.BasePath).NotEmpty().HttpUrl();
    }
}
