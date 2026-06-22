// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.Services.CashIn.Responses;

/// <summary>
/// The outcome of a wallet payment initiation via <c>IPaymobCashInService.StartAsync(PaymobWalletCashInRequest)</c>.
/// </summary>
/// <param name="RedirectUrl">
/// The URL to redirect the customer to for OTP entry and payment confirmation with their wallet provider.
/// </param>
/// <param name="OrderId">The Paymob-assigned order ID, for correlation with callback notifications.</param>
/// <param name="Expiration">The payment key lifetime in seconds.</param>
public sealed record PaymobWalletCashInResponse(string RedirectUrl, string OrderId, int Expiration);
