// Copyright (c) Mahmoud Shaheen, 2021. All rights reserved.

using System.Text.Json.Serialization;
using Framework.Payments.Paymob.CashIn.Models.Merchant;

namespace Framework.Payments.Paymob.CashIn.Models.Auth;

[PublicAPI]
public sealed class CashInAuthenticationTokenResponse
{
    [JsonPropertyName("token")]
    public required string Token { get; init; }

    [JsonPropertyName("profile")]
    public CashInProfile? Profile { get; init; }

    [JsonExtensionData]
    public IDictionary<string, object?>? ExtensionData { get; init; }
}
