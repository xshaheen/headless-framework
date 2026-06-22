// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.Services.CashOut.Requests;

/// <summary>Parameters for a CashOut disbursement to a Vodafone Cash mobile wallet.</summary>
/// <param name="Amount">The amount to disburse in Egyptian Pounds (EGP). Must be positive.</param>
/// <param name="PhoneNumber">The recipient's Vodafone Cash phone number.</param>
public sealed record VodafoneCashOutRequest(decimal Amount, string PhoneNumber);
