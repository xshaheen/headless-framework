// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.Services.CashIn.Requests;

public sealed record PaymobCashInCustomerData(string FirstName, string LastName, string PhoneNumber, string Email);
