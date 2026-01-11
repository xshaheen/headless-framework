// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Payments.Paymob.CashIn.Models.Orders;
using Framework.Urls;

namespace Framework.Payments.Paymob.CashIn;

public partial class PaymobCashInBroker
{
    /// <summary>Create order. Order is a logical container for a transaction(s).</summary>
    public async Task<CashInCreateOrderResponse> CreateOrderAsync(
        CashInCreateOrderRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var authToken = await authenticator.GetAuthenticationTokenAsync().AnyContext();
        var requestUrl = Url.Combine(_options.ApiBaseUrl, "ecommerce/orders");
        var internalRequest = new CashInCreateOrderInternalRequest(authToken, request);

        return await _PostAsync<CashInCreateOrderInternalRequest, CashInCreateOrderResponse>(
            requestUrl,
            internalRequest,
            cancellationToken
        );
    }
}
