// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Payments.Paymob.CashIn.Models.Callback;
using Framework.Payments.Paymob.CashIn.Models.Intentions;
using Framework.Payments.Paymob.CashIn.Models.Refunds;

namespace Framework.Payments.Paymob.CashIn;

public partial class PaymobCashInBroker
{
    public async Task<CashInCreateIntentionResponse?> CreateIntentionAsync(
        CashInCreateIntentionRequest request,
        CancellationToken cancellationToken = default
    )
    {
        return await _PostWithTokenAuthAsync<CashInCreateIntentionRequest, CashInCreateIntentionResponse>(
            _options.CreateIntentionUrl,
            request,
            cancellationToken
        );
    }

    public async Task<CashInCallbackTransaction?> RefundTransactionAsync(
        CashInRefundRequest request,
        CancellationToken cancellationToken = default
    )
    {
        return await _PostWithTokenAuthAsync<CashInRefundRequest, CashInCallbackTransaction>(
            _options.RefundUrl,
            request,
            cancellationToken
        );
    }

    public async Task<CashInCallbackTransaction?> VoidTransactionAsync(
        CashInVoidRefundRequest request,
        CancellationToken cancellationToken = default
    )
    {
        return await _PostWithTokenAuthAsync<CashInVoidRefundRequest, CashInCallbackTransaction>(
            _options.VoidRefundUrl,
            request,
            cancellationToken
        );
    }
}
