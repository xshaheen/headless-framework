// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Payments.Paymob.CashIn.Models;
using Microsoft.Extensions.Options;

namespace Framework.Payments.Paymob.CashIn;

public sealed partial class PaymobCashInBroker(
    HttpClient httpClient,
    IPaymobCashInAuthenticator authenticator,
    IOptionsMonitor<PaymobCashInOptions> options
) : IPaymobCashInBroker
{
    private PaymobCashInOptions Options => options.CurrentValue;
}
