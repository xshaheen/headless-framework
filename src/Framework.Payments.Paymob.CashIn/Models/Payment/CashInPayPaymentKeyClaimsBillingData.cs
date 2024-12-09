// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Payments.Paymob.CashIn.Models.Payment;

[PublicAPI]
public sealed class CashInPayPaymentKeyClaimsBillingData
{
    [JsonPropertyName("first_name")]
    public required string FirstName { get; init; }

    [JsonPropertyName("last_name")]
    public required string LastName { get; init; }

    [JsonPropertyName("email")]
    public required string Email { get; init; }

    [JsonPropertyName("phone_number")]
    public required string PhoneNumber { get; init; }

    [JsonPropertyName("country")]
    public string Country { get; init; } = "NA";

    [JsonPropertyName("state")]
    public string State { get; init; } = "NA";

    [JsonPropertyName("city")]
    public string City { get; init; } = "NA";

    [JsonPropertyName("street")]
    public string Street { get; init; } = "NA";

    [JsonPropertyName("building")]
    public string Building { get; init; } = "NA";

    [JsonPropertyName("floor")]
    public string Floor { get; init; } = "NA";

    [JsonPropertyName("apartment")]
    public string Apartment { get; init; } = "NA";

    [JsonPropertyName("postal_code")]
    public string PostalCode { get; init; } = "NA";

    [JsonPropertyName("extra_description")]
    public string ExtraDescription { get; init; } = "NA";
}
