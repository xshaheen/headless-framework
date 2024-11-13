// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Payments.Paymob.CashIn.Models;
using Microsoft.Extensions.Options;

namespace Framework.Payments.Paymob.CashIn;

public partial class PaymobCashInBroker(
    HttpClient httpClient,
    IPaymobCashInAuthenticator authenticator,
    IOptionsMonitor<PaymobCashInOptions> optionsAccessor
) : IPaymobCashInBroker
{
    private readonly PaymobCashInOptions _options = optionsAccessor.CurrentValue;
}
