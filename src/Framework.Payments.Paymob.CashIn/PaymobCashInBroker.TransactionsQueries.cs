// Copyright (c) Mahmoud Shaheen, 2021. All rights reserved.

using System.Net.Http.Json;
using Flurl;
using Framework.Payments.Paymob.CashIn.Models;
using Framework.Payments.Paymob.CashIn.Models.Transactions;

namespace Framework.Payments.Paymob.CashIn;

public partial class PaymobCashInBroker
{
    public async Task<CashInTransactionsPage?> GetTransactionsPageAsync(CashInTransactionsPageRequest? request = null)
    {
        var authToken = await authenticator.GetAuthenticationTokenAsync();
        var requestUrl = Url.Combine(_options.ApiBaseUrl, "acceptance/transactions");

        if (request is not null)
        {
            requestUrl = requestUrl.SetQueryParams(request.Query);
        }

        using var requestMessage = new HttpRequestMessage();

        requestMessage.Method = HttpMethod.Get;
        requestMessage.RequestUri = new Uri(requestUrl, UriKind.Absolute);
        requestMessage.Headers.Add("Authorization", $"Bearer {authToken}");

        using var response = await httpClient.SendAsync(requestMessage);

        if (!response.IsSuccessStatusCode)
        {
            await PaymobCashInException.ThrowAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<CashInTransactionsPage>();
    }

    public async Task<CashInTransaction?> GetTransactionAsync(string transactionId)
    {
        var authToken = await authenticator.GetAuthenticationTokenAsync();
        var requestUrl = Url.Combine(_options.ApiBaseUrl, $"acceptance/transactions/{transactionId}");

        using var request = new HttpRequestMessage();

        request.Method = HttpMethod.Get;
        request.RequestUri = new Uri(requestUrl, UriKind.Absolute);
        request.Headers.Add("Authorization", $"Bearer {authToken}");

        using var response = await httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            await PaymobCashInException.ThrowAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<CashInTransaction>();
    }
}
