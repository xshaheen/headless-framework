// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Payments.Paymob.CashIn.Models.Orders;
using Headless.Urls;

namespace Headless.Payments.Paymob.CashIn;

public partial class PaymobCashInBroker
{
    /// <summary>Create order. Order is a logical container for a transaction(s).</summary>
    public async Task<CashInCreateOrderResponse> CreateOrderAsync(
        CashInCreateOrderRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var authToken = await authenticator.GetAuthenticationTokenAsync(cancellationToken).ConfigureAwait(false);
        var requestUrl = Url.Combine(Options.ApiBaseUrl, "ecommerce/orders");
        var internalRequest = new CashInCreateOrderInternalRequest(authToken, request);

        return await _PostAsync<CashInCreateOrderInternalRequest, CashInCreateOrderResponse>(
                requestUrl,
                internalRequest,
                cancellationToken
            )
            .ConfigureAwait(false);
    }
}
