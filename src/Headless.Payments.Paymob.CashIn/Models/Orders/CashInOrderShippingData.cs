// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.CashIn.Models.Orders;

[PublicAPI]
public sealed class CashInOrderShippingData
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("first_name")]
    public required string FirstName { get; init; }

    [JsonPropertyName("last_name")]
    public required string LastName { get; init; }

    [JsonPropertyName("email")]
    public required string Email { get; init; }

    [JsonPropertyName("phone_number")]
    public required string PhoneNumber { get; init; }

    [JsonPropertyName("street")]
    public string Street { get; init; } = "NA";

    [JsonPropertyName("building")]
    public string Building { get; init; } = "NA";

    [JsonPropertyName("floor")]
    public string Floor { get; init; } = "NA";

    [JsonPropertyName("apartment")]
    public string Apartment { get; init; } = "NA";

    [JsonPropertyName("city")]
    public string City { get; init; } = "NA";

    [JsonPropertyName("state")]
    public string State { get; init; } = "NA";

    [JsonPropertyName("country")]
    public string Country { get; init; } = "NA";

    [JsonPropertyName("postal_code")]
    public string PostalCode { get; init; } = "NA";

    [JsonPropertyName("order_id")]
    public int OrderId { get; init; }

    [JsonPropertyName("order")]
    public int Order { get; init; }

    [JsonPropertyName("extra_description")]
    public string ExtraDescription { get; init; } = "NA";

    [JsonPropertyName("shipping_method")]
    public string ShippingMethod { get; init; } = "UNK";

    [JsonExtensionData]
    public IDictionary<string, object?>? ExtensionData { get; set; }
}
