// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Sms.Cequens;

public sealed class CequensSmsOptions
{
    public string SingleSmsEndpoint { get; init; } = "https://apis.cequens.com/sms/v1/messages";

    public string TokenEndpoint { get; init; } = "https://apis.cequens.com/auth/v1/tokens";

    public required string ApiKey { get; init; }

    public required string UserName { get; init; }

    public required string SenderName { get; init; }

    public string? Token { get; init; }
}

[UsedImplicitly]
internal sealed class CequensSmsOptionsValidator : AbstractValidator<CequensSmsOptions>
{
    public CequensSmsOptionsValidator()
    {
        RuleFor(x => x.SingleSmsEndpoint).NotEmpty().HttpsOnlyUrl();
        RuleFor(x => x.TokenEndpoint).NotEmpty().HttpsOnlyUrl();
        RuleFor(x => x.ApiKey).NotEmpty();
        RuleFor(x => x.UserName).NotEmpty();
        RuleFor(x => x.SenderName).NotEmpty();
    }
}
