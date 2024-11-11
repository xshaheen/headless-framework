// Copyright (c) Mahmoud Shaheen, 2021. All rights reserved.

using System.Net.Http.Json;
using Flurl;
using Framework.Payments.Paymob.CashIn.Models;
using Framework.Payments.Paymob.CashIn.Models.Orders;

namespace Framework.Payments.Paymob.CashIn;

public partial class PaymobCashInBroker
{
    public async Task<CashInOrdersPage?> GetOrdersPageAsync(CashInOrdersPageRequest? request = null)
    {
        var authToken = await authenticator.GetAuthenticationTokenAsync();
        var requestUrl = Url.Combine(_options.ApiBaseUrl, "ecommerce/orders");

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

        return await response.Content.ReadFromJsonAsync<CashInOrdersPage>();
    }

    public async Task<CashInOrder?> GetOrderAsync(string orderId)
    {
        var authToken = await authenticator.GetAuthenticationTokenAsync();
        var requestUrl = Url.Combine(_options.ApiBaseUrl, "ecommerce/orders", orderId);

        using var request = new HttpRequestMessage();

        request.Method = HttpMethod.Get;
        request.RequestUri = new Uri(requestUrl, UriKind.Absolute);
        request.Headers.Add("Authorization", $"Bearer {authToken}");

        using var response = await httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            await PaymobCashInException.ThrowAsync(response);
        }

        return await response.Content.ReadFromJsonAsync<CashInOrder>();
    }
}
