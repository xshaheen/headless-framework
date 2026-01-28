// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Payments.Paymob.CashIn.Models.Callback;
using Headless.Payments.Paymob.CashIn.Models.Intentions;
using Headless.Payments.Paymob.CashIn.Models.Refunds;

namespace Headless.Payments.Paymob.CashIn;

public partial class PaymobCashInBroker
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
            .AnyContext();
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
            .AnyContext();
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
            .AnyContext();
    }
}
