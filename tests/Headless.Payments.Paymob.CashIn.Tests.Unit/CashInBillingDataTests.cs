// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Payments.Paymob.CashIn;

namespace Tests;

public sealed class CashInBillingDataTests
{
    [Fact]
    public void should_use_wire_compatible_defaults()
    {
        var billingData = new CashInBillingData("First", "Last", "+201234567890", "private@example.com");

        billingData.Country.Should().Be("NA");
        billingData.State.Should().Be("NA");
        billingData.City.Should().Be("NA");
        billingData.Apartment.Should().Be("NA");
        billingData.Street.Should().Be("NA");
        billingData.Floor.Should().Be("NA");
        billingData.Building.Should().Be("NA");
        billingData.ShippingMethod.Should().Be("NA");
        billingData.PostalCode.Should().Be("NA");
    }

    [Fact]
    public void should_not_expose_billing_pii_in_string_representation()
    {
        const string email = "private@example.com";
        const string phoneNumber = "+201234567890";
        var billingData = new CashInBillingData("First", "Last", phoneNumber, email);

        var text = billingData.ToString();

        text.Should().Be(nameof(CashInBillingData));
        text.Should().NotContain(email).And.NotContain(phoneNumber).And.NotContain("First").And.NotContain("Last");
    }
}
