// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Headers;
using System.Net.Http.Json;
using Headless.Checks;
using Headless.Payments.Paymob.CashOut.Internal;
using Headless.Payments.Paymob.CashOut.Models;
using Headless.Urls;
using Microsoft.Extensions.Options;

namespace Headless.Payments.Paymob.CashOut;

internal sealed class PaymobCashOutBroker(
    HttpClient httpClient,
    IPaymobCashOutAuthenticator authenticator,
    IOptionsMonitor<PaymobCashOutOptions> options
) : IPaymobCashOutBroker
{
    private PaymobCashOutOptions Options => options.CurrentValue;

    public async Task<CashOutTransaction> DisburseAsync(
        CashOutDisburseRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var accessToken = await authenticator.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var requestUrl = Url.Combine(Options.ApiBaseUrl, "disburse");

        using var requestMessage = new HttpRequestMessage();

        requestMessage.Method = HttpMethod.Post;
        requestMessage.RequestUri = new Uri(requestUrl, UriKind.Absolute);
        requestMessage.Content = JsonContent.Create(request, options: CashOutJsonOptions.JsonOptions);
        requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await httpClient.SendAsync(requestMessage, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            await PaymobCashOutException.ThrowAsync(response, cancellationToken).ConfigureAwait(false);
        }

        return (
            await response
                .Content.ReadFromJsonAsync<CashOutTransaction>(CashOutJsonOptions.JsonOptions, cancellationToken)
                .ConfigureAwait(false)
        )!;
    }

    /// <summary>Get the budget of the Paymob CashOut account.</summary>
    /// <remarks>API limit is 5 requests per minute.</remarks>
    public async Task<CashOutBudgetResponse> GetBudgetAsync(CancellationToken cancellationToken = default)
    {
        var accessToken = await authenticator.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);

        using var request = new HttpRequestMessage();

        request.Method = HttpMethod.Get;
        request.RequestUri = new Uri(Url.Combine(Options.ApiBaseUrl, "budget/inquire/"), UriKind.Absolute);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            await PaymobCashOutException.ThrowAsync(response, cancellationToken).ConfigureAwait(false);
        }

        return (
            await response
                .Content.ReadFromJsonAsync<CashOutBudgetResponse>(CashOutJsonOptions.JsonOptions, cancellationToken)
                .ConfigureAwait(false)
        )!;
    }

    /// <summary>Get transactions by their Ids.</summary>
    /// <remarks>API limit is 5 requests per minute.</remarks>
    public async Task<CashOutGetTransactionsResponse> GetTransactionsAsync(
        IReadOnlyList<string> transactionsIds,
        bool isBankTransactions,
        int page = 1,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(transactionsIds);
        Argument.IsPositive(page);

        var accessToken = await authenticator.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);

        using var request = new HttpRequestMessage();

        request.Method = HttpMethod.Get;
        request.RequestUri = Url.Combine(Options.ApiBaseUrl, "transaction/inquire/")
            .SetQueryParam("page", page.ToString(CultureInfo.InvariantCulture))
            .ToUri();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        request.Content = JsonContent.Create(
            new CashOutGetTransactionsRequest
            {
                TransactionsIds = transactionsIds,
                IsBankTransactions = isBankTransactions,
            },
            options: CashOutJsonOptions.JsonOptions
        );

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            await PaymobCashOutException.ThrowAsync(response, cancellationToken).ConfigureAwait(false);
        }

        return (
            await response
                .Content.ReadFromJsonAsync<CashOutGetTransactionsResponse>(
                    CashOutJsonOptions.JsonOptions,
                    cancellationToken
                )
                .ConfigureAwait(false)
        )!;
    }
}
