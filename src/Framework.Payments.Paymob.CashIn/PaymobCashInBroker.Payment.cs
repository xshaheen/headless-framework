// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Framework.Payments.Paymob.CashIn.Models.Payment;
using Framework.Urls;

namespace Framework.Payments.Paymob.CashIn;

public partial class PaymobCashInBroker
{
    public string CreateIframeSrc(string iframeId, string token)
    {
        return Url.Combine(_options.IframeBaseUrl, iframeId).SetQueryParams(new { payment_token = token });
    }

    public async Task<CashInWalletPayResponse> CreateWalletPayAsync(
        string paymentKey,
        string phoneNumber,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(paymentKey);
        Argument.IsNotNullOrEmpty(phoneNumber);

        var request = new CashInPayRequest { Source = CashInSource.Wallet(phoneNumber), PaymentToken = paymentKey };

        return await _PayAsync<CashInWalletPayResponse>(request, cancellationToken);
    }

    public async Task<CashInKioskPayResponse> CreateKioskPayAsync(
        string paymentKey,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(paymentKey);

        var request = new CashInPayRequest { Source = CashInSource.Kiosk, PaymentToken = paymentKey };

        return await _PayAsync<CashInKioskPayResponse>(request, cancellationToken);
    }

    public async Task<CashInCashCollectionPayResponse> CreateCashCollectionPayAsync(
        string paymentKey,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(paymentKey);

        var request = new CashInPayRequest { Source = CashInSource.Cash, PaymentToken = paymentKey };

        return await _PayAsync<CashInCashCollectionPayResponse>(request, cancellationToken);
    }

    public async Task<CashInSavedTokenPayResponse> CreateSavedTokenPayAsync(
        string paymentKey,
        string savedToken,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(paymentKey);
        Argument.IsNotNullOrEmpty(savedToken);

        var request = new CashInPayRequest { Source = CashInSource.SavedToken(savedToken), PaymentToken = paymentKey };

        return await _PayAsync<CashInSavedTokenPayResponse>(request, cancellationToken);
    }

    private async Task<TResponse> _PayAsync<TResponse>(CashInPayRequest request, CancellationToken cancellationToken)
    {
        var requestUrl = Url.Combine(_options.ApiBaseUrl, "acceptance/payments/pay");

        return await _PostAsync<CashInPayRequest, TResponse>(requestUrl, request, cancellationToken);
    }
}
