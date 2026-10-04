// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.CashIn;

internal partial class PaymobCashInBroker
{
    public async Task<CashInCreateIntentionResponse?> CreateIntentionAsync(
        CashInCreateIntentionRequest request,
        CancellationToken cancellationToken = default
    )
    {
        return await _PostWithTokenAuthAsync<CashInCreateIntentionRequest, CashInCreateIntentionResponse>(
                Options.CreateIntentionUrl,
                request,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async Task<CashInCallbackTransaction?> RefundTransactionAsync(
        CashInRefundRequest request,
        CancellationToken cancellationToken = default
    )
    {
        return await _PostWithTokenAuthAsync<CashInRefundRequest, CashInCallbackTransaction>(
                Options.RefundUrl,
                request,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async Task<CashInCallbackTransaction?> VoidTransactionAsync(
        CashInVoidRefundRequest request,
        CancellationToken cancellationToken = default
    )
    {
        return await _PostWithTokenAuthAsync<CashInVoidRefundRequest, CashInCallbackTransaction>(
                Options.VoidRefundUrl,
                request,
                cancellationToken
            )
            .ConfigureAwait(false);
    }
}
