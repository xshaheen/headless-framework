// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Payments.Paymob.CashOut.Models;

namespace Tests;

public sealed class PaymobCashOutOptionsTests
{
    [Theory]
    [InlineData("http://paymob.example.com/api")]
    [InlineData("http://10.0.0.10/api")]
    [InlineData("https://user:password@paymob.example.com/api")]
    [InlineData("not-a-url")]
    public void should_reject_unsafe_credential_endpoint(string endpoint)
    {
        var options = _Options(endpoint);

        var result = new PaymobCashOutOptionsValidator().Validate(options);

        result.Errors.Should().ContainSingle(x => x.PropertyName == nameof(PaymobCashOutOptions.ApiBaseUrl));
    }

    [Theory]
    [InlineData("http://localhost:5000/api")]
    [InlineData("http://127.0.0.1:5000/api")]
    [InlineData("http://[::1]:5000/api")]
    public void should_allow_loopback_http_credential_endpoint(string endpoint)
    {
        var options = _Options(endpoint);

        var result = new PaymobCashOutOptionsValidator().Validate(options);

        result.IsValid.Should().BeTrue();
    }

    private static PaymobCashOutOptions _Options(string endpoint)
    {
        return new PaymobCashOutOptions
        {
            ApiBaseUrl = endpoint,
            UserName = "username",
            Password = "password",
            ClientId = "client-id",
            ClientSecret = "client-secret",
        };
    }
}
