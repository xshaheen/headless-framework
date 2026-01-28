// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Payments.Paymob.CashIn.Models.Payment;

[PublicAPI]
public sealed class CashInBillingData
{
    public CashInBillingData(
        string firstName,
        string lastName,
        string phoneNumber,
        string email,
        string country = "NA",
        string state = "NA",
        string city = "NA",
        string apartment = "NA",
        string street = "NA",
        string floor = "NA",
        string building = "NA",
        string shippingMethod = "NA",
        string postalCode = "NA"
    )
    {
        Argument.IsNotNullOrEmpty(firstName);
        Argument.IsNotNullOrEmpty(lastName);
        Argument.IsNotNullOrEmpty(email);
        Argument.IsNotNullOrEmpty(phoneNumber);
        Argument.IsNotNullOrEmpty(country);
        Argument.IsNotNullOrEmpty(state);
        Argument.IsNotNullOrEmpty(city);
        Argument.IsNotNullOrEmpty(apartment);
        Argument.IsNotNullOrEmpty(street);
        Argument.IsNotNullOrEmpty(floor);
        Argument.IsNotNullOrEmpty(building);
        Argument.IsNotNullOrEmpty(shippingMethod);
        Argument.IsNotNullOrEmpty(postalCode);

        Email = email;
        FirstName = firstName;
        LastName = lastName;
        PhoneNumber = phoneNumber;
        Country = country;
        State = state;
        City = city;
        Apartment = apartment;
        Street = street;
        Floor = floor;
        Building = building;
        ShippingMethod = shippingMethod;
        PostalCode = postalCode;
    }

    [JsonPropertyName("email")]
    public string Email { get; init; }

    [JsonPropertyName("first_name")]
    public string FirstName { get; init; }

    [JsonPropertyName("last_name")]
    public string LastName { get; init; }

    [JsonPropertyName("phone_number")]
    public string PhoneNumber { get; init; }

    [JsonPropertyName("country")]
    public string Country { get; }

    [JsonPropertyName("state")]
    public string State { get; }

    [JsonPropertyName("city")]
    public string City { get; }

    [JsonPropertyName("apartment")]
    public string Apartment { get; }

    [JsonPropertyName("street")]
    public string Street { get; }

    [JsonPropertyName("floor")]
    public string Floor { get; }

    [JsonPropertyName("building")]
    public string Building { get; }

    [JsonPropertyName("shipping_method")]
    public string ShippingMethod { get; }

    [JsonPropertyName("postal_code")]
    public string PostalCode { get; }
}
