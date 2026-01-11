// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Payments.Paymob.CashIn.Models.Payment;
using Framework.Urls;

namespace Framework.Payments.Paymob.CashIn;

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
        var authToken = await authenticator.GetAuthenticationTokenAsync().AnyContext();
        var requestUrl = Url.Combine(_options.ApiBaseUrl, "acceptance/payment_keys");
        var internalRequest = new CashInPaymentKeyInternalRequest(request, authToken, _options.ExpirationPeriod);

        return await _PostAsync<CashInPaymentKeyInternalRequest, CashInPaymentKeyResponse>(
            requestUrl,
            internalRequest,
            cancellationToken
        );
    }
}
