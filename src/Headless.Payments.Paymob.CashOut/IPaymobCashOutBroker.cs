// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Json;
using Headless.Checks;
using Headless.Payments.Paymob.CashOut.Internals;
using Headless.Payments.Paymob.CashOut.Models;
using Headless.Urls;

namespace Headless.Payments.Paymob.CashOut;

/// <summary>
/// Low-level HTTP broker for the Paymob CashOut API, which sends money to recipient accounts
/// via mobile wallets, bank cards, or kiosk networks.
/// </summary>
/// <remarks>
/// All methods authenticate automatically using <c>IPaymobCashOutAuthenticator.GetAccessTokenAsync</c>.
/// Errors from the API throw <c>PaymobCashOutException</c>, which carries the HTTP status code and
/// raw response body.
///
/// Use the static factory methods on <c>CashOutDisburseRequest</c> (e.g., <c>CashOutDisburseRequest.Vodafone</c>,
/// <c>CashOutDisburseRequest.BankCard</c>) to build correctly-typed disburse requests rather than
/// constructing the record manually.
///
/// Registered as scoped by <c>SetupPaymobCashOut.AddPaymobCashOut</c>; the underlying
/// <c>HttpClient</c> is injected by the typed-client factory.
/// </remarks>
public interface IPaymobCashOutBroker
{
    /// <summary>
    /// Sends a disbursement to a recipient via the specified channel (wallet, bank card, or kiosk).
    /// </summary>
    /// <param name="request">
    /// The disburse request built with one of the <c>CashOutDisburseRequest</c> factory methods:
    /// <c>Vodafone</c>, <c>Etisalat</c>, <c>Orange</c>, <c>BankWallet</c>, <c>BankCard</c>, or <c>Accept</c>.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// The transaction result from Paymob. Check <c>CashOutTransaction.IsSuccess</c>,
    /// <c>IsPending</c>, and <c>IsFailed</c> to determine the disbursement outcome.
    /// </returns>
    /// <exception cref="PaymobCashOutException">The HTTP request to Paymob failed.</exception>
    [Pure]
    Task<CashOutTransaction> Disburse(CashOutDisburseRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the current available balance (budget) of the Paymob CashOut account.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The raw JSON string returned by the Paymob budget inquiry endpoint.</returns>
    /// <remarks>Paymob rate-limits this endpoint to 5 requests per minute.</remarks>
    /// <exception cref="PaymobCashOutException">The HTTP request to Paymob failed.</exception>
    [Pure]
    Task<string> GetBudgetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the status of one or more disbursement transactions by their Paymob transaction IDs.
    /// </summary>
    /// <param name="transactionsIds">The list of Paymob transaction IDs to look up. Must not be empty.</param>
    /// <param name="isBankTransactions">
    /// <see langword="true"/> when the IDs refer to bank-card or bank-wallet transactions;
    /// <see langword="false"/> for mobile-wallet transactions.
    /// </param>
    /// <param name="page">The 1-based page number for paginated results. Must be positive.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The raw JSON string returned by the Paymob transaction inquiry endpoint.</returns>
    /// <remarks>Paymob rate-limits this endpoint to 5 requests per minute.</remarks>
    /// <exception cref="PaymobCashOutException">The HTTP request to Paymob failed.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="transactionsIds"/> is <see langword="null"/> or empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="page"/> is not positive.</exception>
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
        var accessToken = await authenticator.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var requestUrl = Url.Combine(httpClient.BaseAddress?.ToString()!, "disburse");

        using var requestMessage = new HttpRequestMessage();

        requestMessage.Method = HttpMethod.Post;
        requestMessage.RequestUri = new Uri(requestUrl);
        requestMessage.Content = JsonContent.Create(request, options: CashOutJsonOptions.JsonOptions);
        requestMessage.Headers.Add("Authorization", $"Bearer {accessToken}");

        using var response = await httpClient.SendAsync(requestMessage, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            await PaymobCashOutException.ThrowAsync(response).ConfigureAwait(false);
        }

        return (
            await response
                .Content.ReadFromJsonAsync<CashOutTransaction>(CashOutJsonOptions.JsonOptions, cancellationToken)
                .ConfigureAwait(false)
        )!;
    }

    /// <summary>Get the budget of the Paymob CashOut account.</summary>
    /// <remarks>API limit is 5 requests per minute.</remarks>
    public async Task<string> GetBudgetAsync(CancellationToken cancellationToken = default)
    {
        var accessToken = await authenticator.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);

        using var request = new HttpRequestMessage();

        request.Method = HttpMethod.Get;
        request.RequestUri = new Uri("budget/inquire/", UriKind.Relative);
        request.Headers.Add("Authorization", $"Bearer {accessToken}");

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            await PaymobCashOutException.ThrowAsync(response).ConfigureAwait(false);
        }

        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
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

        var accessToken = await authenticator.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);

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

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            await PaymobCashOutException.ThrowAsync(response).ConfigureAwait(false);
        }

        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }
}
