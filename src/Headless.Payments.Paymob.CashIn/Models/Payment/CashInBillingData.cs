// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Payments.Paymob.CashIn.Models.Payment;

/// <summary>Represents the customer billing data sent with a Paymob CashIn payment request.</summary>
[PublicAPI]
public sealed class CashInBillingData
{
    private const string _NotAvailable = "NA";

    /// <summary>Creates billing data with the required customer identity and contact values.</summary>
    /// <param name="firstName">The customer's first name.</param>
    /// <param name="lastName">The customer's last name.</param>
    /// <param name="phoneNumber">The customer's phone number.</param>
    /// <param name="email">The customer's email address.</param>
    /// <exception cref="ArgumentException">A required value is empty.</exception>
    /// <exception cref="ArgumentNullException">A required value is <see langword="null"/>.</exception>
    public CashInBillingData(string firstName, string lastName, string phoneNumber, string email)
    {
        Argument.IsNotNullOrEmpty(firstName);
        Argument.IsNotNullOrEmpty(lastName);
        Argument.IsNotNullOrEmpty(email);
        Argument.IsNotNullOrEmpty(phoneNumber);
        Email = email;
        FirstName = firstName;
        LastName = lastName;
        PhoneNumber = phoneNumber;
    }

    /// <summary>Gets the customer's email address.</summary>
    [JsonPropertyName("email")]
    public string Email { get; init; }

    /// <summary>Gets the customer's first name.</summary>
    [JsonPropertyName("first_name")]
    public string FirstName { get; init; }

    /// <summary>Gets the customer's last name.</summary>
    [JsonPropertyName("last_name")]
    public string LastName { get; init; }

    /// <summary>Gets the customer's phone number.</summary>
    [JsonPropertyName("phone_number")]
    public string PhoneNumber { get; init; }

    /// <summary>Gets the billing country. The default is <c>NA</c>.</summary>
    [JsonPropertyName("country")]
    public string Country
    {
        get;
        init => field = Argument.IsNotNullOrEmpty(value);
    } = _NotAvailable;

    /// <summary>Gets the billing state. The default is <c>NA</c>.</summary>
    [JsonPropertyName("state")]
    public string State
    {
        get;
        init => field = Argument.IsNotNullOrEmpty(value);
    } = _NotAvailable;

    /// <summary>Gets the billing city. The default is <c>NA</c>.</summary>
    [JsonPropertyName("city")]
    public string City
    {
        get;
        init => field = Argument.IsNotNullOrEmpty(value);
    } = _NotAvailable;

    /// <summary>Gets the billing apartment. The default is <c>NA</c>.</summary>
    [JsonPropertyName("apartment")]
    public string Apartment
    {
        get;
        init => field = Argument.IsNotNullOrEmpty(value);
    } = _NotAvailable;

    /// <summary>Gets the billing street. The default is <c>NA</c>.</summary>
    [JsonPropertyName("street")]
    public string Street
    {
        get;
        init => field = Argument.IsNotNullOrEmpty(value);
    } = _NotAvailable;

    /// <summary>Gets the billing floor. The default is <c>NA</c>.</summary>
    [JsonPropertyName("floor")]
    public string Floor
    {
        get;
        init => field = Argument.IsNotNullOrEmpty(value);
    } = _NotAvailable;

    /// <summary>Gets the billing building. The default is <c>NA</c>.</summary>
    [JsonPropertyName("building")]
    public string Building
    {
        get;
        init => field = Argument.IsNotNullOrEmpty(value);
    } = _NotAvailable;

    /// <summary>Gets the shipping method. The default is <c>NA</c>.</summary>
    [JsonPropertyName("shipping_method")]
    public string ShippingMethod
    {
        get;
        init => field = Argument.IsNotNullOrEmpty(value);
    } = _NotAvailable;

    /// <summary>Gets the billing postal code. The default is <c>NA</c>.</summary>
    [JsonPropertyName("postal_code")]
    public string PostalCode
    {
        get;
        init => field = Argument.IsNotNullOrEmpty(value);
    } = _NotAvailable;

    /// <inheritdoc/>
    public override string ToString()
    {
        return nameof(CashInBillingData);
    }
}
