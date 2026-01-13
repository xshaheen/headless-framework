// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Flurl;
using Framework.Http;
using Framework.Payments.Paymob.CashOut.Internals;
using Framework.Payments.Paymob.CashOut.Models;
using Microsoft.Extensions.Options;

namespace Framework.Payments.Paymob.CashOut;

public interface IPaymobCashOutAuthenticator
{
    [Pure]
    Task<string> GetAccessTokenAsync();
}

public sealed class PaymobCashOutAuthenticator(
    HttpClient httpClient,
    IOptionsMonitor<PaymobCashOutOptions> optionsAccessor
) : IPaymobCashOutAuthenticator
{
    public async Task<string> GetAccessTokenAsync()
    {
        var response = await GenerateTokenAsync();

        return response.AccessToken;
    }

    public async Task<CashOutAuthenticationResponse> GenerateTokenAsync()
    {
        var options = optionsAccessor.CurrentValue;

        using var request = new HttpRequestMessage();

        request.Method = HttpMethod.Post;
        request.RequestUri = new Uri("o/token/", UriKind.Relative);
        request.Content = new FormUrlEncodedContent(
            [new("grant_type", "password"), new("username", options.UserName), new("password", options.Password)]
        );
        request.Headers.Authorization = AuthenticationHeaderFactory.CreateBasic(options.ClientId, options.ClientSecret);

        var response = await httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            await PaymobCashOutException.ThrowAsync(response);
        }

        await using var stream = await response.Content.ReadAsStreamAsync();

        var cashOutAuthenticationResponse = await JsonSerializer.DeserializeAsync<CashOutAuthenticationResponse>(
            stream,
            CashOutJsonOptions.JsonOptions
        );

        return cashOutAuthenticationResponse!;
    }

    public async Task<CashOutAuthenticationResponse> RefreshTokenAsync(string refreshToken)
    {
        var options = optionsAccessor.CurrentValue;
        var requestUrl = Url.Combine(optionsAccessor.CurrentValue.ApiBaseUrl, "o/token");

        using var request = new HttpRequestMessage();

        request.Method = HttpMethod.Post;
        request.RequestUri = new Uri(requestUrl, UriKind.Absolute);
        request.Headers.Authorization = AuthenticationHeaderFactory.CreateBasic(options.ClientId, options.ClientSecret);
        request.Content = new FormUrlEncodedContent(
            [new("grant_type", "refresh_token"), new("refresh_token", refreshToken)]
        );

        var response = await httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            await PaymobCashOutException.ThrowAsync(response);
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        return (
            await JsonSerializer.DeserializeAsync<CashOutAuthenticationResponse>(stream, CashOutJsonOptions.JsonOptions)
        )!;
    }
}
