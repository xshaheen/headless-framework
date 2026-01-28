// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.Services.CashOut.Requests;

public sealed record KioskCashOutRequest(
    decimal Amount,
    string PhoneNumber,
    string FirstName,
    string LastName,
    string Email
);
