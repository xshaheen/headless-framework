// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Payments.Paymob.CashIn.Models.Transactions;
using Headless.Urls;

namespace Headless.Payments.Paymob.CashIn;

public partial class PaymobCashInBroker
{
    public async Task<CashInTransactionsPage?> GetTransactionsPageAsync(
        CashInTransactionsPageRequest? request = null,
        CancellationToken cancellationToken = default
    )
    {
        var requestUrl = Url.Combine(Options.ApiBaseUrl, "acceptance/transactions");

        if (request is not null)
        {
            requestUrl = requestUrl.SetQueryParams(request.Query);
        }

        return await _GetWithBearerAuthAsync<CashInTransactionsPage>(requestUrl, cancellationToken).AnyContext();
    }

    public async Task<CashInTransaction?> GetTransactionAsync(
        string transactionId,
        CancellationToken cancellationToken = default
    )
    {
        var requestUrl = Url.Combine(Options.ApiBaseUrl, $"acceptance/transactions/{transactionId}");

        return await _GetWithBearerAuthAsync<CashInTransaction>(requestUrl, cancellationToken).AnyContext();
    }
}
