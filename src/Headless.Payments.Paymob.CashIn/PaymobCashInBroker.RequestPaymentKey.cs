// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Payments.Paymob.CashIn.Models.Payment;
using Headless.Urls;

namespace Headless.Payments.Paymob.CashIn;

public partial class PaymobCashInBroker
{
    /// <summary>
    /// Get a payment key which is used to authenticate payment request and verifying transaction
    /// request metadata.
    /// </summary>
    public async Task<CashInPaymentKeyResponse> RequestPaymentKeyAsync(
        CashInPaymentKeyRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var authToken = await authenticator.GetAuthenticationTokenAsync(cancellationToken).AnyContext();
        var requestUrl = Url.Combine(Options.ApiBaseUrl, "acceptance/payment_keys");
        var internalRequest = new CashInPaymentKeyInternalRequest(request, authToken, Options.ExpirationPeriod);

        return await _PostAsync<CashInPaymentKeyInternalRequest, CashInPaymentKeyResponse>(
                requestUrl,
                internalRequest,
                cancellationToken
            )
            .AnyContext();
    }
}
