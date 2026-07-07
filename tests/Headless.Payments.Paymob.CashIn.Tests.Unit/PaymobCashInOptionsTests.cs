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
}
