// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Sms.Connekio;

public sealed class ConnekioSmsOptions
{
    public string SingleSmsEndpoint { get; init; } = "https://api.connekio.com/sms/single";

    public string BatchSmsEndpoint { get; init; } = "https://api.connekio.com/sms/batch";

    public required string Sender { get; init; }

    public required string AccountId { get; init; }

    public required string UserName { get; init; }

    public required string Password { get; init; }
}

[UsedImplicitly]
internal sealed class ConnekioSmsOptionsValidator : AbstractValidator<ConnekioSmsOptions>
{
    public ConnekioSmsOptionsValidator()
    {
        RuleFor(x => x.SingleSmsEndpoint).NotEmpty().HttpsOnlyUrl();
        RuleFor(x => x.BatchSmsEndpoint).NotEmpty().HttpsOnlyUrl();
        RuleFor(x => x.Sender).NotEmpty();
        RuleFor(x => x.AccountId).NotEmpty();
        RuleFor(x => x.UserName).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
    }
}
