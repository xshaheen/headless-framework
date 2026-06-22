// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.Services.CashIn.Responses;

/// <summary>
/// The outcome of a successful card payment initiation via <c>IPaymobCashInService.StartAsync(PaymobCardCashInRequest)</c>.
/// </summary>
/// <param name="IframeSrc">
/// The full embed URL for the Paymob hosted card-payment iframe. Render this in an
/// <c>&lt;iframe src="..."&gt;</c> element to display the payment form.
/// </param>
/// <param name="PaymentKey">The raw payment key string, usable directly with the Paymob JS SDK.</param>
/// <param name="OrderId">The Paymob-assigned order ID, for correlation with callback notifications.</param>
/// <param name="Expiration">The payment key lifetime in seconds.</param>
public sealed record PaymobCardCashInResponse(string IframeSrc, string PaymentKey, string OrderId, int Expiration);
