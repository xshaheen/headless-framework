// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.Services.CashIn.Requests;

/// <summary>
/// Parameters for voiding a Paymob transaction that occurred on the same business day.
/// </summary>
/// <param name="TransactionId">The Paymob transaction ID to void. No fees apply to void operations.</param>
public sealed record PaymobVoidRequest(string TransactionId);
