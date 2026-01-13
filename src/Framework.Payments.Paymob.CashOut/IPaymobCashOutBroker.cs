// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Json;
using System.Text.Encodings.Web;
using Flurl;
using Framework.Checks;
using Framework.Payments.Paymob.CashOut.Internals;
using Framework.Payments.Paymob.CashOut.Models;

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
    public async Task<CashOutTransaction> Disburse(CashOutDisburseRequest request)
    {
        var accessToken = await authenticator.GetAccessTokenAsync();
        var requestUrl = Url.Combine(httpClient.BaseAddress?.ToString()!, "disburse");

        using var requestMessage = new HttpRequestMessage();

        requestMessage.Method = HttpMethod.Post;
        requestMessage.RequestUri = new Uri(requestUrl);
        requestMessage.Content = JsonContent.Create(request, options: CashOutJsonOptions.JsonOptions);
        requestMessage.Headers.Add("Authorization", $"Bearer {accessToken}");

        var response = await httpClient.SendAsync(requestMessage);

        if (!response.IsSuccessStatusCode)
        {
            await PaymobCashOutException.ThrowAsync(response);
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        return (await JsonSerializer.DeserializeAsync<CashOutTransaction>(stream, CashOutJsonOptions.JsonOptions))!;
    }

    /// <summary>Get the budget of the Paymob CashOut account.</summary>
    /// <remarks>API limit is 5 requests per minute.</remarks>
    public async Task<string> GetBudgetAsync()
    {
        var accessToken = await authenticator.GetAccessTokenAsync();

        using var request = new HttpRequestMessage();

        request.Method = HttpMethod.Get;
        request.RequestUri = new Uri("budget/inquire/", UriKind.Relative);
        request.Headers.Add("Authorization", $"Bearer {accessToken}");

        var response = await httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            await PaymobCashOutException.ThrowAsync(response);
        }

        return await response.Content.ReadAsStringAsync();
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

        var accessToken = await authenticator.GetAccessTokenAsync();

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
            },
            options: CashOutJsonOptions.JsonOptions
        );

        var response = await httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            await PaymobCashOutException.ThrowAsync(response);
        }

        return await response.Content.ReadAsStringAsync();
    }
}
