// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.CashOut.Models;

/// <summary>
/// The OAuth 2.0 token response returned by the Paymob CashOut authentication endpoint.
/// </summary>
[PublicAPI]
public sealed record CashOutAuthenticationResponse
{
    /// <summary>The bearer access token used to authorise CashOut API requests.</summary>
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    /// <summary>
    /// The refresh token that can be exchanged for a new access token via
    /// <c>IPaymobCashOutAuthenticator.RefreshTokenAsync</c>.
    /// </summary>
    [JsonPropertyName("refresh_token")]
    public required string RefreshToken { get; init; }

    /// <summary>The OAuth scopes granted to this token.</summary>
    [JsonPropertyName("scope")]
    public required string Scope { get; init; }

    /// <summary>The token type string (typically <c>Bearer</c>).</summary>
    [JsonPropertyName("token_type")]
    public required string TokenType { get; init; }

    /// <summary>The access token lifetime in seconds as reported by the server.</summary>
    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; init; }

    [JsonExtensionData]
    public IDictionary<string, object?>? ExtensionData { get; init; }
}
