// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Framework.Payments.Paymob.CashOut.Models;

[PublicAPI]
public sealed class PaymobCashOutOptions
{
    public required string ApiBaseUrl { get; init; }

    public required string UserName { get; init; }

    public required string Password { get; init; }

    public required string ClientId { get; init; }

    public required string ClientSecret { get; init; }

    /// <summary>
    /// Token refresh buffer. Auth tokens are cached for this duration.
    /// Default is 10 minutes.
    /// </summary>
    public TimeSpan TokenRefreshBuffer { get; set; } = TimeSpan.FromMinutes(10);
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
        RuleFor(x => x.TokenRefreshBuffer)
            .GreaterThan(TimeSpan.Zero)
            .LessThanOrEqualTo(TimeSpan.FromMinutes(60))
            .WithMessage("TokenRefreshBuffer must be positive and at most 60 minutes");
    }
}
