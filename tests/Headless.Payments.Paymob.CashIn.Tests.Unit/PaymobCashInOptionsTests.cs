// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Payments.Paymob.CashIn.Models;

namespace Tests;

public sealed class PaymobCashInOptionsTests
{
    [Fact]
    public void should_redact_secrets_from_to_string()
    {
        // given
        var options = new PaymobCashInOptions
        {
            ApiKey = "api-key-value-xyz",
            Hmac = "hmac-value-xyz",
            SecretKey = "secret-key-value-xyz",
        };

        // when
        var text = options.ToString();

        // then
        text.Should().NotContain("api-key-value-xyz");
        text.Should().NotContain("hmac-value-xyz");
        text.Should().NotContain("secret-key-value-xyz");
        text.Should().Contain("ApiKey = ***");
        text.Should().Contain("Hmac = ***");
        text.Should().Contain("SecretKey = ***");
        // non-secret configuration is still printed
        text.Should().Contain("ApiBaseUrl = ");
    }

    [Theory]
    [InlineData("http://paymob.example.com/api")]
    [InlineData("http://10.0.0.10/api")]
    [InlineData("https://user:password@paymob.example.com/api")]
    [InlineData("not-a-url")]
    public void should_reject_unsafe_credential_endpoints(string endpoint)
    {
        var options = _OptionsWithEndpoints(endpoint);

        var result = new PaymobCashInOptionsValidator().Validate(options);

        result
            .Errors.Select(x => x.PropertyName)
            .Should()
            .BeEquivalentTo([
                nameof(PaymobCashInOptions.ApiBaseUrl),
                nameof(PaymobCashInOptions.IframeBaseUrl),
                nameof(PaymobCashInOptions.CreateIntentionUrl),
                nameof(PaymobCashInOptions.RefundUrl),
                nameof(PaymobCashInOptions.VoidRefundUrl),
            ]);
    }

    [Theory]
    [InlineData("http://localhost:5000/api")]
    [InlineData("http://127.0.0.1:5000/api")]
    [InlineData("http://[::1]:5000/api")]
    public void should_allow_loopback_http_credential_endpoints(string endpoint)
    {
        var options = _OptionsWithEndpoints(endpoint);

        var result = new PaymobCashInOptionsValidator().Validate(options);

        result.IsValid.Should().BeTrue();
    }

    private static PaymobCashInOptions _OptionsWithEndpoints(string endpoint)
    {
        return new PaymobCashInOptions
        {
            ApiBaseUrl = endpoint,
            IframeBaseUrl = endpoint,
            CreateIntentionUrl = endpoint,
            RefundUrl = endpoint,
            VoidRefundUrl = endpoint,
            ApiKey = "api-key",
            Hmac = "hmac",
            SecretKey = "secret-key",
        };
    }
}
