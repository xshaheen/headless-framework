// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.Services.CashOut.Requests;

public sealed record VodafoneCashOutRequest(decimal Amount, string PhoneNumber);
