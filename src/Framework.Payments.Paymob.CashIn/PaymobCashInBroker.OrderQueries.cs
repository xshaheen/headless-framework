// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Payments.Paymob.CashIn.Internals;
using Framework.Payments.Paymob.CashIn.Models;
using Framework.Payments.Paymob.CashIn.Models.Orders;
using Framework.Urls;

namespace Framework.Payments.Paymob.CashIn;

public partial class PaymobCashInBroker
{
    public async Task<CashInOrdersPage?> GetOrdersPageAsync(
        CashInOrdersPageRequest? request = null,
        CancellationToken cancellationToken = default
    )
    {
        var requestUrl = Url.Combine(Options.ApiBaseUrl, "ecommerce/orders");

        if (request is not null)
        {
            requestUrl = requestUrl.SetQueryParams(request.Query);
        }

        return await _GetWithBearerAuthAsync<CashInOrdersPage>(requestUrl, cancellationToken).AnyContext();
    }

    public async Task<CashInOrder?> GetOrderAsync(string orderId, CancellationToken cancellationToken = default)
    {
        var requestUrl = Url.Combine(Options.ApiBaseUrl, "ecommerce/orders", orderId);

        return await _GetWithBearerAuthAsync<CashInOrder>(requestUrl, cancellationToken).AnyContext();
    }
}
