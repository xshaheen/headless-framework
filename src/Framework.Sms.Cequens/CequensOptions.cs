// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Framework.FluentValidation;

namespace Framework.Sms.Cequens;

public sealed class CequensOptions
{
    public required string Uri { get; init; }

    public required string ApiKey { get; init; }

    public required string UserName { get; init; }

    public required string SenderName { get; init; }

    public required string Token { get; init; }
}

internal sealed class CequensOptionsValidator : AbstractValidator<CequensOptions>
{
    public CequensOptionsValidator()
    {
        RuleFor(x => x.Uri).NotEmpty().HttpUrl();
        RuleFor(x => x.ApiKey).NotEmpty();
        RuleFor(x => x.UserName).NotEmpty();
        RuleFor(x => x.SenderName).NotEmpty();
        RuleFor(x => x.Token).NotEmpty();
    }
}
