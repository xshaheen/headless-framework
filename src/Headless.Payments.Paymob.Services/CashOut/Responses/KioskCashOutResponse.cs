// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.Services.CashOut.Responses;

/// <summary>
/// The outcome of a successful CashOut disbursement via the Aman kiosk (Accept) channel.
/// </summary>
/// <param name="TransactionId">The Paymob-assigned transaction identifier.</param>
/// <param name="Status">
/// The disbursement status: <c>Success</c> when the transfer completed, or <c>Pending</c>
/// when awaiting the recipient's collection at the kiosk.
/// </param>
/// <param name="BillingReference">
/// The Aman billing reference number the recipient presents at the kiosk outlet to collect cash.
/// </param>
public sealed record KioskCashOutResponse(string TransactionId, CashOutResponseStatus Status, string BillingReference);
