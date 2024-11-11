// Copyright (c) Mahmoud Shaheen, 2021. All rights reserved.

using System.Net.Http.Json;
using Flurl;
using Framework.Kernel.Checks;
using Framework.Payments.Paymob.CashIn.Models;
using Framework.Payments.Paymob.CashIn.Models.Payment;

namespace Framework.Payments.Paymob.CashIn;

public partial class PaymobCashInBroker
{
    public string CreateIframeSrc(string iframeId, string token)
    {
        return Url.Combine(_options.IframeBaseUrl, iframeId).SetQueryParams(new { payment_token = token });
    }

    public async Task<CashInWalletPayResponse> CreateWalletPayAsync(string paymentKey, string phoneNumber)
    {
        Argument.IsNotNullOrEmpty(paymentKey);
        Argument.IsNotNullOrEmpty(phoneNumber);

        var request = new CashInPayRequest { Source = CashInSource.Wallet(phoneNumber), PaymentToken = paymentKey };

        return await _PayAsync<CashInWalletPayResponse>(request);
    }

    public async Task<CashInKioskPayResponse> CreateKioskPayAsync(string paymentKey)
    {
        Argument.IsNotNullOrEmpty(paymentKey);

        var request = new CashInPayRequest { Source = CashInSource.Kiosk, PaymentToken = paymentKey };

        return await _PayAsync<CashInKioskPayResponse>(request);
    }

    public async Task<CashInCashCollectionPayResponse> CreateCashCollectionPayAsync(string paymentKey)
    {
        Argument.IsNotNullOrEmpty(paymentKey);

        var request = new CashInPayRequest { Source = CashInSource.Cash, PaymentToken = paymentKey };

        return await _PayAsync<CashInCashCollectionPayResponse>(request);
    }

    public async Task<CashInSavedTokenPayResponse> CreateSavedTokenPayAsync(string paymentKey, string savedToken)
    {
        Argument.IsNotNullOrEmpty(paymentKey);
        Argument.IsNotNullOrEmpty(savedToken);

        var request = new CashInPayRequest { Source = CashInSource.SavedToken(savedToken), PaymentToken = paymentKey };

        return await _PayAsync<CashInSavedTokenPayResponse>(request);
    }

    private async Task<TResponse> _PayAsync<TResponse>(CashInPayRequest request)
    {
        var requestUrl = Url.Combine(_options.ApiBaseUrl, "acceptance/payments/pay");
        using var response = await httpClient.PostAsJsonAsync(requestUrl, request);

        if (!response.IsSuccessStatusCode)
        {
            await PaymobCashInException.ThrowAsync(response);
        }

        return (await response.Content.ReadFromJsonAsync<TResponse>())!;
    }
}
