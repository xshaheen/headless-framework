// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Urls;

namespace Headless.Payments.Paymob.CashIn;

internal partial class PaymobCashInBroker
{
    public string CreateIframeSrc(string iframeId, string token)
    {
        return Url.Combine(Options.IframeBaseUrl, iframeId).SetQueryParams(new { payment_token = token });
    }

    public async Task<PaymobCashInResult<CashInWalletPayResponse>> CreateWalletPayAsync(
        string paymentKey,
        string phoneNumber,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(paymentKey);
        Argument.IsNotNullOrEmpty(phoneNumber);

        var request = new CashInPayRequest { Source = CashInSource.Wallet(phoneNumber), PaymentToken = paymentKey };

        return await _PayAsync<CashInWalletPayResponse>(
                request,
                static response => _ClassifyTransaction(response.Success, response.Pending),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async Task<PaymobCashInResult<CashInKioskPayResponse>> CreateKioskPayAsync(
        string paymentKey,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(paymentKey);

        var request = new CashInPayRequest { Source = CashInSource.Kiosk, PaymentToken = paymentKey };

        return await _PayAsync<CashInKioskPayResponse>(
                request,
                static response => _ClassifyTransaction(response.Success, response.Pending),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async Task<PaymobCashInResult<CashInCashCollectionPayResponse>> CreateCashCollectionPayAsync(
        string paymentKey,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(paymentKey);

        var request = new CashInPayRequest { Source = CashInSource.Cash, PaymentToken = paymentKey };

        return await _PayAsync<CashInCashCollectionPayResponse>(
                request,
                static response => _ClassifyTransaction(_IsTrue(response.Success), _IsTrue(response.Pending)),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async Task<PaymobCashInResult<CashInSavedTokenPayResponse>> CreateSavedTokenPayAsync(
        string paymentKey,
        string savedToken,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(paymentKey);
        Argument.IsNotNullOrEmpty(savedToken);

        var request = new CashInPayRequest { Source = CashInSource.SavedToken(savedToken), PaymentToken = paymentKey };

        return await _PayAsync<CashInSavedTokenPayResponse>(
                request,
                static response => _ClassifyTransaction(_IsTrue(response.Success), _IsTrue(response.Pending)),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private async Task<PaymobCashInResult<TResponse>> _PayAsync<TResponse>(
        CashInPayRequest request,
        Func<TResponse, PaymobCashInOutcome> classify,
        CancellationToken cancellationToken
    )
    {
        var requestUrl = Url.Combine(Options.ApiBaseUrl, "acceptance/payments/pay");

        return await _PostAsync(requestUrl, request, classify, cancellationToken).ConfigureAwait(false);
    }
}
