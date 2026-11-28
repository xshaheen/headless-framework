// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Urls;

namespace Headless.Payments.Paymob.CashIn;

internal partial class PaymobCashInBroker
{
    public async Task<PaymobCashInResult<CashInTransactionsPage>> GetTransactionsPageAsync(
        CashInTransactionsPageRequest? request = null,
        CancellationToken cancellationToken = default
    )
    {
        var requestUrl = Url.Combine(Options.ApiBaseUrl, "acceptance/transactions");

        if (request is not null)
        {
            requestUrl = requestUrl.SetQueryParams(request.Query);
        }

        return await _GetWithBearerAuthAsync<CashInTransactionsPage>(requestUrl, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<PaymobCashInResult<CashInTransaction>> GetTransactionAsync(
        string transactionId,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(transactionId);

        var requestUrl = Url.Combine(Options.ApiBaseUrl, $"acceptance/transactions/{transactionId}");

        return await _GetWithBearerAuthAsync<CashInTransaction>(requestUrl, cancellationToken).ConfigureAwait(false);
    }
}
