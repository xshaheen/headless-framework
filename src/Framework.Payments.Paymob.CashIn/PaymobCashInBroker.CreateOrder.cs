// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flurl;
using Framework.Payments.Paymob.CashIn.Models;
using Framework.Payments.Paymob.CashIn.Models.Orders;

namespace Framework.Payments.Paymob.CashIn;

public partial class PaymobCashInBroker
{
    private static readonly JsonSerializerOptions _IgnoreNullOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Create order. Order is a logical container for a transaction(s).</summary>
    public async Task<CashInCreateOrderResponse> CreateOrderAsync(CashInCreateOrderRequest request)
    {
        var authToken = await authenticator.GetAuthenticationTokenAsync();
        var requestUrl = Url.Combine(_options.ApiBaseUrl, "ecommerce/orders");
        var internalRequest = new CashInCreateOrderInternalRequest(authToken, request);

        using var response = await httpClient.PostAsJsonAsync(requestUrl, internalRequest, _IgnoreNullOptions);

        if (!response.IsSuccessStatusCode)
        {
            await PaymobCashInException.ThrowAsync(response);
        }

        return (await response.Content.ReadFromJsonAsync<CashInCreateOrderResponse>())!;
    }
}
