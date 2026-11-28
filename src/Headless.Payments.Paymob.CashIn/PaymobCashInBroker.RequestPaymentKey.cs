// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Payments.Paymob.CashIn.Internal;
using Headless.Urls;

namespace Headless.Payments.Paymob.CashIn;

internal partial class PaymobCashInBroker
{
    /// <summary>
    /// Get a payment key which is used to authenticate payment request and verifying transaction
    /// request metadata.
    /// </summary>
    public async Task<PaymobCashInResult<CashInPaymentKeyResponse>> RequestPaymentKeyAsync(
        CashInPaymentKeyRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);

        var authToken = await authenticator.GetAuthenticationTokenAsync(cancellationToken).ConfigureAwait(false);

        if (!authToken.IsSucceeded)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return PaymobCashInHttp.NotSent<CashInPaymentKeyResponse>(authToken.Error);
        }

        var requestUrl = Url.Combine(Options.ApiBaseUrl, "acceptance/payment_keys");
        var internalRequest = new CashInPaymentKeyInternalRequest(request, authToken.Value, Options.ExpirationPeriod);

        return await _PostAsync<CashInPaymentKeyInternalRequest, CashInPaymentKeyResponse>(
                requestUrl,
                internalRequest,
                classify: null,
                cancellationToken
            )
            .ConfigureAwait(false);
    }
}
