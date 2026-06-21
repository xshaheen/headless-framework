// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.Services.CashIn.Requests;

/// <summary>
/// Parameters for refunding a previously captured Paymob transaction.
/// </summary>
/// <param name="TransactionId">The Paymob transaction ID to refund.</param>
/// <param name="Amount">
/// The amount to refund in Egyptian Pounds (EGP). May be less than the original charge for
/// partial refunds. The value is converted to cents internally before submission.
/// </param>
public sealed record PaymobRefundRequest(string TransactionId, decimal Amount);
