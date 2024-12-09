// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Payments.Paymob.CashIn.Models.Payment;

[PublicAPI]
public sealed class CashInSource
{
    public static readonly CashInSource Kiosk = new("AGGREGATOR", "AGGREGATOR");
    public static readonly CashInSource Cash = new("cash", "CASH");

    public static CashInSource Wallet(string phoneNumber) => new(phoneNumber, "WALLET");

    public static CashInSource SavedToken(string savedToken) => new(savedToken, "TOKEN");

    private CashInSource(string identifier, string subtype)
    {
        Identifier = identifier;
        Subtype = subtype;
    }

    [JsonPropertyName("identifier")]
    public string Identifier { get; init; }

    [JsonPropertyName("subtype")]
    public string Subtype { get; init; }
}
