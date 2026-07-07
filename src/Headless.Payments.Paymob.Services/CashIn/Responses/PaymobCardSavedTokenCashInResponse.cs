// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.Services.CashIn.Responses;

/// <summary>
/// The outcome of a saved-card token charge via <c>IPaymobCashInService.StartAsync(PaymobCardSavedTokenCashInRequest)</c>.
/// </summary>
public sealed record PaymobCardSavedTokenCashInResponse
{
    /// <summary>The Paymob-assigned transaction ID.</summary>
    public required long TransactionId { get; init; }

    /// <summary>The Paymob-assigned order ID.</summary>
    public required string OrderId { get; init; }

    /// <summary>
    /// <see langword="true"/> when the card issuer requires 3-D Secure authentication.
    /// Redirect the customer to <c>RedirectionUrl</c> to complete the OTP step.
    /// </summary>
    public required bool Is3DSecure { get; init; }

    /// <summary>
    /// <see langword="true"/> when the charge was approved without requiring 3-D Secure.
    /// When <see langword="false"/> and <c>Is3DSecure</c> is also <see langword="false"/>, the charge was declined.
    /// </summary>
    public required bool IsSuccess { get; init; }

    /// <summary>
    /// The 3-D Secure redirect URL to present to the customer when <c>Is3DSecure</c> is
    /// <see langword="true"/>. <see langword="null"/> for non-3DS outcomes.
    /// </summary>
    public required string? RedirectionUrl { get; init; }
}
