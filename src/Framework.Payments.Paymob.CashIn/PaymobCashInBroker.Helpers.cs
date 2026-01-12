// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Json;
using Framework.Payments.Paymob.CashIn.Models;

namespace Framework.Payments.Paymob.CashIn;

public partial class PaymobCashInBroker
{
    private async Task<TResponse> _PostAsync<TRequest, TResponse>(
        string url,
        TRequest request,
        CancellationToken cancellationToken = default
    )
    {
        using var response = await httpClient
            .PostAsJsonAsync(url, request, _options.SerializationOptions, cancellationToken)
            .AnyContext();

        if (!response.IsSuccessStatusCode)
        {
            await PaymobCashInException.ThrowAsync(response, cancellationToken).AnyContext();
        }

        var result = await response
            .Content.ReadFromJsonAsync<TResponse>(_options.DeserializationOptions, cancellationToken)
            .AnyContext();

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
        var authToken = await authenticator.GetAuthenticationTokenAsync().AnyContext();

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
        httpRequest.Headers.Add("Authorization", $"Bearer {authToken}");
        httpRequest.Content = JsonContent.Create(request, options: _options.SerializationOptions);

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken).AnyContext();

        if (!response.IsSuccessStatusCode)
        {
            await PaymobCashInException.ThrowAsync(response, cancellationToken).AnyContext();
        }

        var result = await response
            .Content.ReadFromJsonAsync<TResponse>(_options.DeserializationOptions, cancellationToken)
            .AnyContext();

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
        httpRequest.Headers.Add("Authorization", $"Token {_options.SecretKey}");
        httpRequest.Content = JsonContent.Create(request, options: _options.SerializationOptions);

        using var response = await httpClient.SendAsync(httpRequest, cancellationToken).AnyContext();

        if (!response.IsSuccessStatusCode)
        {
            await PaymobCashInException.ThrowAsync(response, cancellationToken).AnyContext();
        }

        return await response
            .Content.ReadFromJsonAsync<TResponse>(_options.DeserializationOptions, cancellationToken)
            .AnyContext();
    }

    private async Task<TResponse?> _GetWithBearerAuthAsync<TResponse>(
        string url,
        CancellationToken cancellationToken = default
    )
    {
        var authToken = await authenticator.GetAuthenticationTokenAsync().AnyContext();

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Authorization", $"Bearer {authToken}");

        using var response = await httpClient.SendAsync(request, cancellationToken).AnyContext();

        if (!response.IsSuccessStatusCode)
        {
            await PaymobCashInException.ThrowAsync(response, cancellationToken).AnyContext();
        }

        return await response
            .Content.ReadFromJsonAsync<TResponse>(_options.DeserializationOptions, cancellationToken)
            .AnyContext();
    }
}
