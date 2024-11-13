// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Payments.Paymob.Services.CashOut.Requests;

public sealed record EtisalatCashOutRequest(decimal Amount, string PhoneNumber);
