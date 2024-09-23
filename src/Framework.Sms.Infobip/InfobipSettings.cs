// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using FluentValidation;
using Framework.FluentValidation;

namespace Framework.Sms.Infobip;

public sealed class InfobipSettings
{
    public required string Sender { get; set; }

    public required string ApiKey { get; set; }

    public required string BasePath { get; set; }
}

internal sealed class InfobipSettingsValidator : AbstractValidator<InfobipSettings>
{
    public InfobipSettingsValidator()
    {
        RuleFor(x => x.Sender).NotEmpty();
        RuleFor(x => x.ApiKey).NotEmpty();
        RuleFor(x => x.BasePath).NotEmpty().HttpUrl();
    }
}
