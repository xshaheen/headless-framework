// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Json;
using System.Text.Encodings.Web;
using Framework.Checks;
using Framework.Payments.Paymob.CashOut.Internals;
using Framework.Payments.Paymob.CashOut.Models;
using Framework.Urls;

namespace Framework.Payments.Paymob.CashOut;

public interface IPaymobCashOutBroker
{
    [Pure]
    Task<CashOutTransaction> Disburse(CashOutDisburseRequest request, CancellationToken cancellationToken = default);

    [Pure]
    Task<string> GetBudgetAsync(CancellationToken cancellationToken = default);

    [Pure]
    Task<string> GetTransactionsAsync(
        IReadOnlyList<string> transactionsIds,
        bool isBankTransactions,
        int page = 1,
        CancellationToken cancellationToken = default
    );
}

public sealed class PaymobCashOutBroker(HttpClient httpClient, IPaymobCashOutAuthenticator authenticator)
    : IPaymobCashOutBroker
{
    public async Task<CashOutTransaction> Disburse(
        CashOutDisburseRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var accessToken = await authenticator.GetAccessTokenAsync(cancellationToken).AnyContext();
        var requestUrl = Url.Combine(httpClient.BaseAddress?.ToString()!, "disburse");

        using var requestMessage = new HttpRequestMessage();

        requestMessage.Method = HttpMethod.Post;
        requestMessage.RequestUri = new Uri(requestUrl);
        requestMessage.Content = JsonContent.Create(request, options: CashOutJsonOptions.JsonOptions);
        requestMessage.Headers.Add("Authorization", $"Bearer {accessToken}");

        using var response = await httpClient.SendAsync(requestMessage, cancellationToken).AnyContext();

        if (!response.IsSuccessStatusCode)
        {
            await PaymobCashOutException.ThrowAsync(response).AnyContext();
        }

        return (
            await response
                .Content.ReadFromJsonAsync<CashOutTransaction>(CashOutJsonOptions.JsonOptions, cancellationToken)
                .AnyContext()
        )!;
    }

    /// <summary>Get the budget of the Paymob CashOut account.</summary>
    /// <remarks>API limit is 5 requests per minute.</remarks>
    public async Task<string> GetBudgetAsync(CancellationToken cancellationToken = default)
    {
        var accessToken = await authenticator.GetAccessTokenAsync(cancellationToken).AnyContext();

        using var request = new HttpRequestMessage();

        request.Method = HttpMethod.Get;
        request.RequestUri = new Uri("budget/inquire/", UriKind.Relative);
        request.Headers.Add("Authorization", $"Bearer {accessToken}");

        using var response = await httpClient.SendAsync(request, cancellationToken).AnyContext();

        if (!response.IsSuccessStatusCode)
        {
            await PaymobCashOutException.ThrowAsync(response).AnyContext();
        }

        return await response.Content.ReadAsStringAsync(cancellationToken).AnyContext();
    }

    /// <summary>Get transactions by their Ids.</summary>
    /// <remarks>API limit is 5 requests per minute.</remarks>
    public async Task<string> GetTransactionsAsync(
        IReadOnlyList<string> transactionsIds,
        bool isBankTransactions,
        int page = 1,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(transactionsIds);
        Argument.IsPositive(page);

        var accessToken = await authenticator.GetAccessTokenAsync(cancellationToken).AnyContext();

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

        using var response = await httpClient.SendAsync(request, cancellationToken).AnyContext();

        if (!response.IsSuccessStatusCode)
        {
            await PaymobCashOutException.ThrowAsync(response).AnyContext();
        }

        return await response.Content.ReadAsStringAsync(cancellationToken).AnyContext();
    }
}
