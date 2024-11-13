// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization;

namespace Framework.Payments.Paymob.CashIn.Models.Auth;

[PublicAPI]
internal sealed class CashInAuthenticationTokenRequest
{
    [JsonPropertyName("api_key")]
    public required string ApiKey { get; init; }
}
