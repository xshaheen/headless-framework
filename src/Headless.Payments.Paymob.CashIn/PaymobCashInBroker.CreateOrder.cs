// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Payments.Paymob.CashIn.Internal;
using Headless.Urls;

namespace Headless.Payments.Paymob.CashIn;

internal partial class PaymobCashInBroker
{
    /// <summary>Create order. Order is a logical container for a transaction(s).</summary>
    public async Task<PaymobCashInResult<CashInCreateOrderResponse>> CreateOrderAsync(
        CashInCreateOrderRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);

        var authToken = await authenticator.GetAuthenticationTokenAsync(cancellationToken).ConfigureAwait(false);

        if (!authToken.IsSucceeded)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return PaymobCashInHttp.NotSent<CashInCreateOrderResponse>(authToken.Error);
        }

        var requestUrl = Url.Combine(Options.ApiBaseUrl, "ecommerce/orders");
        var internalRequest = new CashInCreateOrderInternalRequest(authToken.Value, request);

        return await _PostAsync<CashInCreateOrderInternalRequest, CashInCreateOrderResponse>(
                requestUrl,
                internalRequest,
                classify: null,
                cancellationToken
            )
            .ConfigureAwait(false);
    }
}
