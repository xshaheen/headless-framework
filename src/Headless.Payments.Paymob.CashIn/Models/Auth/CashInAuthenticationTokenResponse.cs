// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Payments.Paymob.CashIn.Models.Merchant;

namespace Headless.Payments.Paymob.CashIn.Models.Auth;

[PublicAPI]
public sealed class CashInAuthenticationTokenResponse
{
    [JsonPropertyName("token")]
    public required string Token { get; init; }

    [JsonPropertyName("profile")]
    public CashInProfile? Profile { get; init; }

    [JsonExtensionData]
    public IDictionary<string, object?>? ExtensionData { get; set; }
}
