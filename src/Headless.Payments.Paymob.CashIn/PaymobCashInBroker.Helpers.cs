// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Headers;
using System.Net.Http.Json;
using Headless.Payments.Paymob.CashIn.Internal;

namespace Headless.Payments.Paymob.CashIn;

internal partial class PaymobCashInBroker
{
    private async Task<PaymobCashInResult<TResponse>> _PostAsync<TRequest, TResponse>(
        string url,
        TRequest request,
        Func<TResponse, PaymobCashInOutcome>? classify,
        CancellationToken cancellationToken
    )
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
        httpRequest.Content = JsonContent.Create(request, options: CashInJsonOptions.JsonOptions);

        return await PaymobCashInHttp
            .SendAsync(httpClient, httpRequest, classify, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<PaymobCashInResult<TResponse>> _PostWithTokenAuthAsync<TRequest, TResponse>(
        string url,
        TRequest request,
        Func<TResponse, PaymobCashInOutcome>? classify,
        CancellationToken cancellationToken
    )
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Token", Options.SecretKey);
        httpRequest.Content = JsonContent.Create(request, options: CashInJsonOptions.JsonOptions);

        return await PaymobCashInHttp
            .SendAsync(httpClient, httpRequest, classify, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<PaymobCashInResult<TResponse>> _GetWithBearerAuthAsync<TResponse>(
        string url,
        CancellationToken cancellationToken
    )
    {
        var authToken = await authenticator.GetAuthenticationTokenAsync(cancellationToken).ConfigureAwait(false);

        if (!authToken.IsSucceeded)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return PaymobCashInHttp.NotSent<TResponse>(authToken.Error);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authToken.Value);

        return await PaymobCashInHttp
            .SendAsync<TResponse>(httpClient, request, classify: null, cancellationToken)
            .ConfigureAwait(false);
    }

    // Paymob answers a declined charge, refund, or void with 200 and a transaction whose success and pending
    // flags are both false, so the body decides. A pending transaction (3-D Secure, a wallet OTP, a kiosk bill)
    // was accepted; its final state arrives by callback.
    private static PaymobCashInOutcome _ClassifyTransaction(bool success, bool pending)
    {
        return success || pending ? PaymobCashInOutcome.Succeeded : PaymobCashInOutcome.Rejected;
    }

    private static bool _IsTrue(string? flag)
    {
        return string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase);
    }
}
