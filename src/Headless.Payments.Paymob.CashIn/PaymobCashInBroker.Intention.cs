// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Payments.Paymob.CashIn;

internal partial class PaymobCashInBroker
{
    public async Task<PaymobCashInResult<CashInCreateIntentionResponse>> CreateIntentionAsync(
        CashInCreateIntentionRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);

        return await _PostWithTokenAuthAsync<CashInCreateIntentionRequest, CashInCreateIntentionResponse>(
                Options.CreateIntentionUrl,
                request,
                classify: null,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async Task<PaymobCashInResult<CashInCallbackTransaction>> RefundTransactionAsync(
        CashInRefundRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);

        return await _PostWithTokenAuthAsync<CashInRefundRequest, CashInCallbackTransaction>(
                Options.RefundUrl,
                request,
                static transaction => _ClassifyTransaction(transaction.Success, transaction.Pending),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async Task<PaymobCashInResult<CashInCallbackTransaction>> VoidTransactionAsync(
        CashInVoidRefundRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);

        return await _PostWithTokenAuthAsync<CashInVoidRefundRequest, CashInCallbackTransaction>(
                Options.VoidRefundUrl,
                request,
                static transaction => _ClassifyTransaction(transaction.Success, transaction.Pending),
                cancellationToken
            )
            .ConfigureAwait(false);
    }
}
