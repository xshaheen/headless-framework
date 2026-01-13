// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Json;
using Framework.Checks;
using Framework.Payments.Paymob.CashOut.Models;
using Framework.Urls;

namespace Framework.Payments.Paymob.CashOut;

public interface IPaymobCashOutBroker
{
    [Pure]
    Task<CashOutTransaction> Disburse(CashOutDisburseRequest request);

    [Pure]
    Task<string> GetBudgetAsync();

    [Pure]
    Task<string> GetTransactionsAsync(IReadOnlyList<string> transactionsIds, bool isBankTransactions, int page = 1);
}

public sealed class PaymobCashOutBroker(HttpClient httpClient, IPaymobCashOutAuthenticator authenticator)
    : IPaymobCashOutBroker
{
    private static readonly JsonSerializerOptions _Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<CashOutTransaction> Disburse(CashOutDisburseRequest request)
    {
        var accessToken = await authenticator.GetAccessTokenAsync().AnyContext();
        var requestUrl = Url.Combine(httpClient.BaseAddress?.ToString()!, "disburse");

        using var requestMessage = new HttpRequestMessage();

        requestMessage.Method = HttpMethod.Post;
        requestMessage.RequestUri = new Uri(requestUrl);
        requestMessage.Content = JsonContent.Create(request, options: _Options);
        requestMessage.Headers.Add("Authorization", $"Bearer {accessToken}");

        var response = await httpClient.SendAsync(requestMessage).AnyContext();

        if (!response.IsSuccessStatusCode)
        {
            await PaymobCashOutException.ThrowAsync(response).AnyContext();
        }

        return (await response.Content.ReadFromJsonAsync<CashOutTransaction>().AnyContext())!;
    }

    /// <summary>Get the budget of the Paymob CashOut account.</summary>
    /// <remarks>API limit is 5 requests per minute.</remarks>
    public async Task<string> GetBudgetAsync()
    {
        var accessToken = await authenticator.GetAccessTokenAsync().AnyContext();

        using var request = new HttpRequestMessage();

        request.Method = HttpMethod.Get;
        request.RequestUri = new Uri("budget/inquire/", UriKind.Relative);
        request.Headers.Add("Authorization", $"Bearer {accessToken}");

        var response = await httpClient.SendAsync(request).AnyContext();

        if (!response.IsSuccessStatusCode)
        {
            await PaymobCashOutException.ThrowAsync(response).AnyContext();
        }

        return await response.Content.ReadAsStringAsync().AnyContext();
    }

    /// <summary>Get transactions by their Ids.</summary>
    /// <remarks>API limit is 5 requests per minute.</remarks>
    public async Task<string> GetTransactionsAsync(
        IReadOnlyList<string> transactionsIds,
        bool isBankTransactions,
        int page = 1
    )
    {
        Argument.IsNotNullOrEmpty(transactionsIds);
        Argument.IsPositive(page);

        var accessToken = await authenticator.GetAccessTokenAsync().AnyContext();

        using var request = new HttpRequestMessage();

        request.Method = HttpMethod.Get;
        request.RequestUri = new Uri(
            $"transaction/inquire/?page={page.ToString(CultureInfo.InvariantCulture)}",
            UriKind.Relative
        );
        request.Headers.Add("Authorization", $"Bearer {accessToken}");

        request.Content = JsonContent.Create(
            new CashOutGetTransactionsRequest
            {
                TransactionsIds = transactionsIds,
                IsBankTransactions = isBankTransactions,
            }
        );

        var response = await httpClient.SendAsync(request).AnyContext();

        if (!response.IsSuccessStatusCode)
        {
            await PaymobCashOutException.ThrowAsync(response).AnyContext();
        }

        return await response.Content.ReadAsStringAsync().AnyContext();
    }
}
