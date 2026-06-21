// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.Services.CashOut.Requests;

/// <summary>Parameters for a CashOut disbursement to an Orange Money mobile wallet.</summary>
/// <param name="Amount">The amount to disburse in Egyptian Pounds (EGP). Must be positive.</param>
/// <param name="PhoneNumber">The recipient's Orange Money phone number.</param>
/// <param name="FullName">The recipient's full name.</param>
public sealed record OrangeCashOutRequest(decimal Amount, string PhoneNumber, string FullName);
