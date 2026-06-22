// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.Services.CashOut.Responses;

/// <summary>
/// The outcome of a successful CashOut disbursement for wallet and bank-account channels.
/// </summary>
/// <param name="TransactionId">The Paymob-assigned transaction identifier.</param>
/// <param name="Status">
/// The disbursement status: <c>Success</c> when the transfer completed immediately, or
/// <c>Pending</c> when the provider has accepted the request but processing is ongoing.
/// </param>
public sealed record CashOutResponse(string TransactionId, CashOutResponseStatus Status);
