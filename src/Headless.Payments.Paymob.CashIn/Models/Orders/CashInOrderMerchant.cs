// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Headless.Payments.Paymob.CashIn.Internals;

namespace Headless.Payments.Paymob.CashIn.Models.Orders;

[PublicAPI]
public sealed class CashInOrderMerchant
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("created_at")]
    [JsonConverter(typeof(AddEgyptZoneOffsetToUnspecifiedDateTimeJsonConverter))]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("company_name")]
    public string CompanyName { get; init; } = string.Empty;

    [JsonPropertyName("state")]
    public string State { get; init; } = string.Empty;

    [JsonPropertyName("country")]
    public string Country { get; init; } = string.Empty;

    [JsonPropertyName("city")]
    public string City { get; init; } = string.Empty;

    [JsonPropertyName("postal_code")]
    public string PostalCode { get; init; } = string.Empty;

    [JsonPropertyName("street")]
    public string Street { get; init; } = string.Empty;

    [JsonPropertyName("phones")]
    [field: AllowNull, MaybeNull]
    public IReadOnlyList<string> Phones
    {
        get => field ?? [];
        init;
    }

    [JsonPropertyName("company_emails")]
    [field: AllowNull, MaybeNull]
    public IReadOnlyList<string> CompanyEmails
    {
        get => field ?? [];
        init;
    }

    [JsonExtensionData]
    public IDictionary<string, object?>? ExtensionData { get; set; }
}
