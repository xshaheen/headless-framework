// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Json;
using Headless.Payments.Paymob.CashIn.Internals;
using Headless.Payments.Paymob.CashIn.Models;

namespace Headless.Payments.Paymob.CashIn;

public partial class PaymobCashInBroker
{
    private async Task<TResponse> _PostAsync<TRequest, TResponse>(
        string url,
        TRequest request,
        CancellationToken cancellationToken = default
    )
    {
        using var response = await httpClient
            .PostAsJsonAsync(url, request, CashInJsonOptions.JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            await PaymobCashInException.ThrowAsync(response, cancellationToken).ConfigureAwait(false);
        }

        var result = await response
            .Content.ReadFromJsonAsync<TResponse>(CashInJsonOptions.JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        if (result is null)
        {
            throw new PaymobCashInException("Paymob CashIn returned null response body.", response.StatusCode, null);
        }

        return result;
    }

    private async Task<TResponse> _PostWithBearerAuthAsync<TRequest, TResponse>(
        string url,
        TRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var authToken = await authenticator.GetAuthenticationTokenAsync().ConfigureAwait(false);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
        httpRequest.Headers.Add("Authorization", $"Bearer {authToken}");
        httpRequest.Content = JsonContent.Create(request, options: CashInJsonOptions.JsonOptions);

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            await PaymobCashInException.ThrowAsync(response, cancellationToken).ConfigureAwait(false);
        }

        var result = await response
            .Content.ReadFromJsonAsync<TResponse>(CashInJsonOptions.JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        if (result is null)
        {
            throw new PaymobCashInException("Paymob CashIn returned null response body.", response.StatusCode, null);
        }

        return result;
    }

    private async Task<TResponse?> _PostWithTokenAuthAsync<TRequest, TResponse>(
        string url,
        TRequest request,
        CancellationToken cancellationToken = default
    )
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
        httpRequest.Headers.Add("Authorization", $"Token {Options.SecretKey}");
        httpRequest.Content = JsonContent.Create(request, options: CashInJsonOptions.JsonOptions);

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            await PaymobCashInException.ThrowAsync(response, cancellationToken).ConfigureAwait(false);
        }

        return await response
            .Content.ReadFromJsonAsync<TResponse>(CashInJsonOptions.JsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<TResponse?> _GetWithBearerAuthAsync<TResponse>(
        string url,
        CancellationToken cancellationToken = default
    )
    {
        var authToken = await authenticator.GetAuthenticationTokenAsync(cancellationToken).ConfigureAwait(false);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Authorization", $"Bearer {authToken}");

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            await PaymobCashInException.ThrowAsync(response, cancellationToken).ConfigureAwait(false);
        }

        return await response
            .Content.ReadFromJsonAsync<TResponse>(CashInJsonOptions.JsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }
}
