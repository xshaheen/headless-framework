// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.Services.CashOut.Requests;

/// <summary>
/// Parameters for a CashOut disbursement via the Aman kiosk (Accept) channel. The recipient
/// collects cash at any Aman outlet using the billing reference returned in the response.
/// </summary>
/// <param name="Amount">The amount to disburse in Egyptian Pounds (EGP). Must be positive.</param>
/// <param name="PhoneNumber">The recipient's phone number.</param>
/// <param name="FirstName">The recipient's first name.</param>
/// <param name="LastName">The recipient's last name.</param>
/// <param name="Email">The recipient's email address.</param>
public sealed record KioskCashOutRequest(
    decimal Amount,
    string PhoneNumber,
    string FirstName,
    string LastName,
    string Email
);
