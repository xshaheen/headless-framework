// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Json;
using Framework.Payments.Paymob.CashIn.Models;
using Framework.Payments.Paymob.CashIn.Models.Callback;
using Framework.Payments.Paymob.CashIn.Models.Intentions;
using Framework.Payments.Paymob.CashIn.Models.Refunds;

namespace Framework.Payments.Paymob.CashIn;

public partial class PaymobCashInBroker
{
    public async Task<CashInCreateIntentionResponse?> CreateIntentionAsync(CashInCreateIntentionRequest request)
    {
        return await _SendApiTokenPostAsync<CashInCreateIntentionRequest, CashInCreateIntentionResponse>(
            _options.CreateIntentionUrl,
            request
        );
    }

    public async Task<CashInCallbackTransaction?> RefundTransactionAsync(CashInRefundRequest request)
    {
        return await _SendApiTokenPostAsync<CashInRefundRequest, CashInCallbackTransaction>(
            _options.RefundUrl,
            request
        );
    }

    public async Task<CashInCallbackTransaction?> VoidTransactionAsync(CashInVoidRefundRequest request)
    {
        return await _SendApiTokenPostAsync<CashInVoidRefundRequest, CashInCallbackTransaction>(
            _options.VoidRefundUrl,
            request
        );
    }

    private async Task<TResponse?> _SendApiTokenPostAsync<TRequest, TResponse>(string url, TRequest request)
    {
        using var httpRequestMessage = new HttpRequestMessage(HttpMethod.Post, url);
        httpRequestMessage.Headers.Add("Authorization", $"Token {_options.SecretKey}");
        httpRequestMessage.Content = JsonContent.Create(request, options: _options.SerializationOptions);

        using var response = await httpClient.SendAsync(httpRequestMessage);

        if (!response.IsSuccessStatusCode)
        {
            await PaymobCashInException.ThrowAsync(response);
        }

        var content = await response.Content.ReadFromJsonAsync<TResponse>(_options.DeserializationOptions);

        if (content is null)
        {
            return default;
        }

        return content;
    }
}
