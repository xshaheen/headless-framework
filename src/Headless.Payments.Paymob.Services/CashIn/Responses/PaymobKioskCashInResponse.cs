// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.Services.CashIn.Responses;

/// <summary>
/// The outcome of a kiosk payment initiation via <c>IPaymobCashInService.StartAsync(PaymobKioskCashInRequest)</c>.
/// </summary>
/// <param name="BillingReference">
/// The Aman kiosk bill reference number the customer presents at the outlet to pay.
/// </param>
/// <param name="OrderId">The Paymob-assigned order ID, for correlation with callback notifications.</param>
/// <param name="Expiration">The payment key lifetime in seconds.</param>
public sealed record PaymobKioskCashInResponse(string BillingReference, string OrderId, int Expiration);
