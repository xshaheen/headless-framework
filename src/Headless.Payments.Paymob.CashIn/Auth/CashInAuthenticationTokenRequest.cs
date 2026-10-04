// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.CashIn;

[PublicAPI]
internal sealed class CashInAuthenticationTokenRequest
{
    [JsonPropertyName("api_key")]
    public required string ApiKey { get; init; }
}
