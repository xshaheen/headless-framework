// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Payments.Paymob.CashIn.Models.Merchant;

namespace Headless.Payments.Paymob.CashIn.Models.Auth;

/// <summary>
/// The response from the Paymob authentication endpoint, containing the bearer token used to
/// authorise subsequent API requests.
/// </summary>
[PublicAPI]
public sealed class CashInAuthenticationTokenResponse
{
    /// <summary>The bearer token to supply in the <c>Authorization</c> header of Paymob API requests.</summary>
    [JsonPropertyName("token")]
    public required string Token { get; init; }

    /// <summary>The merchant profile associated with this API key, if returned by Paymob.</summary>
    [JsonPropertyName("profile")]
    public CashInProfile? Profile { get; init; }

    [JsonExtensionData]
    public IDictionary<string, object?>? ExtensionData { get; set; }
}
