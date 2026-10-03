// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Tests.Primitives;

public sealed class PhoneNumberTests
{
    [Fact]
    public void should_be_correctly_initialized_when_phone_number()
    {
        // given
        const int countryCode = 20;
        const string number = "1018541323";

        // when
        var phoneNumber = new PhoneNumber(countryCode, number);

        // then
        phoneNumber.CountryCode.Should().Be(20);
        phoneNumber.Number.Should().Be("1018541323");
        phoneNumber.ToString().Should().Be("+20 1018541323");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void should_throw_when_country_code_is_not_positive(int countryCode)
    {
        // when
        var act = () => new PhoneNumber(countryCode, "1234567890");

        // then
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void should_throw_when_number_is_null_or_empty(string? number)
    {
        // when
        var act = () => new PhoneNumber(1, number!);

        // then
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void should_implement_equality_correctly()
    {
        // given
        var phoneNumber1 = new PhoneNumber(1, "5555555555");
        var phoneNumber2 = new PhoneNumber(1, "5555555555");
        var phoneNumber3 = new PhoneNumber(1, "5555555556");
        var phoneNumber4 = new PhoneNumber(44, "5555555555");

        // then - Equals
        phoneNumber1.Equals(phoneNumber2).Should().BeTrue();
        phoneNumber1.Equals(phoneNumber3).Should().BeFalse();
        phoneNumber1.Equals(phoneNumber4).Should().BeFalse();
        phoneNumber1.Equals((object?)null).Should().BeFalse();

        // then - GetHashCode
        phoneNumber1!.GetHashCode().Should().Be(phoneNumber2.GetHashCode());
        phoneNumber1.GetHashCode().Should().NotBe(phoneNumber3.GetHashCode());

        // then - == operator
        (phoneNumber1 == phoneNumber2)
            .Should()
            .BeTrue();
        (phoneNumber1 == phoneNumber3).Should().BeFalse();
        (phoneNumber1 != phoneNumber3).Should().BeTrue();
    }

    [Theory]
    [InlineData("(555) 123-4567", "5551234567")]
    [InlineData("555 123 4567", "5551234567")]
    [InlineData("5551234567", "5551234567")]
    public void should_canonicalize_number_to_digits_only(string input, string expected)
    {
        // when
        var phoneNumber = new PhoneNumber(1, input);

        // then
        phoneNumber.Number.Should().Be(expected);
    }

    [Fact]
    public void should_ignore_formatting_differences_in_number_when_equality()
    {
        // given - the bug: "555-1234" used to be unequal to "5551234"
        var formatted = new PhoneNumber(1, "555-1234");
        var plain = new PhoneNumber(1, "5551234");

        // then
        formatted.Equals(plain).Should().BeTrue();
        (formatted == plain).Should().BeTrue();
        formatted.GetHashCode().Should().Be(plain.GetHashCode());
    }

    [Fact]
    public void should_not_bypass_country_code_validation_when_object_initializer()
    {
        // when - init accessors validate even when set via an object initializer
        var act = () => new PhoneNumber(1, "1234567890") { CountryCode = -5 };

        // then
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void should_not_bypass_number_validation_when_object_initializer()
    {
        // when
        var act = () => new PhoneNumber(1, "1234567890") { Number = "" };

        // then
        act.Should().Throw<ArgumentException>();
    }
}
