// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Payments.Paymob.CashOut.Models;

/// <summary>
/// Configuration options for the Paymob CashOut integration.
/// </summary>
/// <remarks>
/// Register via <c>SetupPaymobCashOut.AddPaymobCashOut</c>. Options are validated on startup;
/// missing required properties or an invalid URL cause the application to fail fast.
/// The API URL requires HTTPS for external hosts; HTTP is accepted only for loopback development and test servers,
/// and user information is rejected.
/// Authentication uses the OAuth 2.0 password grant; the token is cached and refreshed
/// automatically based on <c>TokenRefreshBuffer</c>.
/// </remarks>
[PublicAPI]
public sealed class PaymobCashOutOptions
{
    /// <summary>
    /// The base URL of the Paymob CashOut API (e.g., <c>https://accept.paymob.com/v1/</c>). External endpoints
    /// require HTTPS; HTTP is accepted only for loopback development and test servers.
    /// </summary>
    public required string ApiBaseUrl { get; init; }

    /// <summary>The merchant username used in the OAuth password-grant request.</summary>
    public required string UserName { get; init; }

    /// <summary>The merchant password used in the OAuth password-grant request.</summary>
    public required string Password { get; init; }

    /// <summary>
    /// The OAuth client ID used in the Basic authentication header of token requests.
    /// Obtain this from the Paymob CashOut dashboard.
    /// </summary>
    public required string ClientId { get; init; }

    /// <summary>
    /// The OAuth client secret used in the Basic authentication header of token requests.
    /// </summary>
    public required string ClientSecret { get; init; }

    /// <summary>
    /// Controls how long the cached access token is considered valid before a refresh is triggered.
    /// Must be positive and at most 60 minutes. Defaults to 10 minutes.
    /// </summary>
    public TimeSpan TokenRefreshBuffer { get; set; } = TimeSpan.FromMinutes(10);
}

internal sealed class PaymobCashOutOptionsValidator : AbstractValidator<PaymobCashOutOptions>
{
    public PaymobCashOutOptionsValidator()
    {
        RuleFor(x => x.ApiBaseUrl).NotEmpty().HttpsOrLoopbackHttpUrl();
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
