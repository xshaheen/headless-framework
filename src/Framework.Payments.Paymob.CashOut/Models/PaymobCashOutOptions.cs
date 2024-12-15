// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Framework.FluentValidation;

namespace Framework.Payments.Paymob.CashOut.Models;

[PublicAPI]
public sealed class PaymobCashOutOptions
{
    public required string ApiBaseUrl { get; init; }

    public required string UserName { get; init; }

    public required string Password { get; init; }

    public required string ClientId { get; init; }

    public required string ClientSecret { get; init; }
}

public sealed class PaymobCashOutOptionsValidator : AbstractValidator<PaymobCashOutOptions>
{
    public PaymobCashOutOptionsValidator()
    {
        RuleFor(x => x.ApiBaseUrl).NotEmpty().HttpUrl();
        RuleFor(x => x.UserName).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
        RuleFor(x => x.ClientId).NotEmpty();
        RuleFor(x => x.ClientSecret).NotEmpty();
    }
}
