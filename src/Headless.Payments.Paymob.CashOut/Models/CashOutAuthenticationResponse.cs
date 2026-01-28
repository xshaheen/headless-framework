// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.CashOut.Models;

[PublicAPI]
public sealed record CashOutAuthenticationResponse
{
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    [JsonPropertyName("refresh_token")]
    public required string RefreshToken { get; init; }

    [JsonPropertyName("scope")]
    public required string Scope { get; init; }

    [JsonPropertyName("token_type")]
    public required string TokenType { get; init; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; init; }

    [JsonExtensionData]
    public IDictionary<string, object?>? ExtensionData { get; init; }
}
