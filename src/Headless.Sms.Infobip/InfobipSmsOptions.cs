// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Sms.Infobip;

public sealed class InfobipSmsOptions
{
    public required string Sender { get; init; }

    public required string ApiKey { get; init; }

    public required string BasePath { get; init; }
}

[UsedImplicitly]
internal sealed class InfobipSmsOptionsValidator : AbstractValidator<InfobipSmsOptions>
{
    public InfobipSmsOptionsValidator()
    {
        RuleFor(x => x.Sender).NotEmpty();
        RuleFor(x => x.ApiKey).NotEmpty();
        RuleFor(x => x.BasePath).NotEmpty().HttpsOnlyUrl();
    }
}
