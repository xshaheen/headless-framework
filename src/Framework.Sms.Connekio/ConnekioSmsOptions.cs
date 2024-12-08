// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Framework.FluentValidation;

namespace Framework.Sms.Connekio;

public sealed class ConnekioSmsOptions
{
    public required string SingleSmsEndpoint { get; init; } = "https://api.connekio.com/sms/single";

    public required string BatchSmsEndpoint { get; init; } = "https://api.connekio.com/sms/batch";

    public required string Sender { get; init; }

    public required string AccountId { get; init; }

    public required string UserName { get; init; }

    public required string Password { get; init; }
}

internal sealed class ConnekioSmsOptionsValidator : AbstractValidator<ConnekioSmsOptions>
{
    public ConnekioSmsOptionsValidator()
    {
        RuleFor(x => x.SingleSmsEndpoint).NotEmpty().HttpUrl();
        RuleFor(x => x.BatchSmsEndpoint).NotEmpty().HttpUrl();
        RuleFor(x => x.Sender).NotEmpty();
        RuleFor(x => x.AccountId).NotEmpty();
        RuleFor(x => x.UserName).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
    }
}
