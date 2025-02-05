// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Payments.Paymob.CashIn.Models.Intentions;

public sealed class CashInCreateIntentionResponseBillingData
{
    [JsonPropertyName("first_name")]
    public required string FirstName { get; init; }

    [JsonPropertyName("last_name")]
    public required string LastName { get; init; }

    [JsonPropertyName("phone_number")]
    public required string PhoneNumber { get; init; }

    [JsonPropertyName("email")]
    public required string Email { get; init; }

    [JsonPropertyName("country")]
    public required string Country { get; init; }

    [JsonPropertyName("apartment")]
    public string? Apartment { get; init; }

    [JsonPropertyName("floor")]
    public string? Floor { get; init; }

    [JsonPropertyName("street")]
    public string? Street { get; init; }

    [JsonPropertyName("city")]
    public string? City { get; init; }

    [JsonPropertyName("building")]
    public string? Building { get; init; }

    [JsonPropertyName("state")]
    public string? State { get; init; }

    [JsonPropertyName("postal_code")]
    public string? PostalCode { get; init; }

    [JsonPropertyName("shipping_method")]
    public string? ShippingMethod { get; init; }
}
