// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Primitives;

namespace Tests.Primitives;

public sealed class PhoneNumberTests
{
    [Fact]
    public void phone_number_should_be_correctly_initialized()
    {
        // given
        const int countryCode = 20;
        const string number = "1018541323";

        // when
        var phoneNumber = new PhoneNumber(countryCode, number);

        // then
        phoneNumber.CountryCode.Should().Be(20);
        phoneNumber.Number.Should().Be("1018541323");
        phoneNumber.ToString().Should().Be("+20 10 18541323");
    }

    [Fact]
    public void normalize_should_return_correct_format()
    {
        // given
        const int countryCode = 20;
        const string number = "1018541323";

        // when
        var normalized = PhoneNumber.Normalize(countryCode, number);

        // then
        normalized.Should().Be("+201018541323");
    }

    [Fact]
    public void get_region_codes_should_return_valid_region()
    {
        // given
        var phoneNumber = new PhoneNumber(20, "1018541323");

        // when
        var result = phoneNumber.GetRegionCodes();

        // then
        result.Should().Be("EG");
    }

    [Theory]
    [InlineData(20, "1018541323", "+201018541323")]
    [InlineData(20, "01018541323", "+201018541323")]
    [InlineData(20, "010 18541 323", "+201018541323")]
    public void normalize_single_string_should_return_correct_format(int code, string number, string expected)
    {
        // when
        var normalized = PhoneNumber.Normalize(code, number);

        // then
        normalized.Should().Be(expected);
    }

    [Fact]
    public void from_international_format_should_return_valid_phone_number()
    {
        // given
        const string internationalNumber = "+20 101-854-1323";

        // when
        var phoneNumber = PhoneNumber.FromInternationalFormat(internationalNumber);

        // then
        phoneNumber.Should().NotBeNull();
        phoneNumber.CountryCode.Should().Be(20);
        phoneNumber.Number.Should().Be("1018541323");
    }

    [Fact]
    public void get_international_and_national_format_should_return_null_for_invalid_number()
    {
        // given
        var phoneNumber = new PhoneNumber(1, "invalid-number");

        // when
        var resultGetInternationalFormat = phoneNumber.GetInternationalFormat();
        var resultGetNationalFormat = phoneNumber.GetNationalFormat();

        // then
        resultGetInternationalFormat.Should().BeNull();
        resultGetNationalFormat.Should().BeNull();
    }

    [Fact]
    public void get_international_format_should_work_as_expected()
    {
        var phoneNumber = new PhoneNumber(20, "01018541323");

        var result = phoneNumber.GetInternationalFormat();

        result.Should().Be("+20 10 18541323");
    }

    [Fact]
    public void get_national_format_should_work_as_expected()
    {
        var phoneNumber = new PhoneNumber(20, "1018541323");

        var result = phoneNumber.GetNationalFormat();

        result.Should().Be("010 18541323");
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

    [Theory]
    [InlineData(1, "5555555555", "(555) 555-5555")] // US
    [InlineData(44, "2079460958", "020 7946 0958")] // UK
    [InlineData(20, "1001234567", "010 01234567")] // Egypt
    public void should_get_national_format(int countryCode, string number, string expectedNational)
    {
        // given
        var phoneNumber = new PhoneNumber(countryCode, number);

        // when
        var result = phoneNumber.GetNationalFormat();

        // then
        result.Should().Be(expectedNational);
    }

    [Theory]
    [InlineData(1, "5555555555", "+1 555-555-5555")] // US
    [InlineData(44, "2079460958", "+44 20 7946 0958")] // UK
    [InlineData(20, "1001234567", "+20 10 01234567")] // Egypt
    public void should_get_international_format(int countryCode, string number, string expectedInternational)
    {
        // given
        var phoneNumber = new PhoneNumber(countryCode, number);

        // when
        var result = phoneNumber.GetInternationalFormat();

        // then
        result.Should().Be(expectedInternational);
    }

    [Theory]
    [InlineData(1, "2025551234", "US")] // US - DC area code (valid)
    [InlineData(44, "2079460958", "GB")] // UK
    [InlineData(20, "1001234567", "EG")] // Egypt
    public void should_get_region_codes(int countryCode, string number, string expectedRegion)
    {
        // given
        var phoneNumber = new PhoneNumber(countryCode, number);

        // when
        var result = phoneNumber.GetRegionCodes();

        // then
        result.Should().Be(expectedRegion);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("123")]
    [InlineData("+999999999999999")]
    public void should_return_null_for_invalid_phone_number(string invalidNumber)
    {
        // when
        var internationalResult = PhoneNumber.GetInternationalFormat(invalidNumber);
        var nationalResult = PhoneNumber.GetNationalFormat(invalidNumber);

        // then
        internationalResult.Should().BeNull();
        nationalResult.Should().BeNull();
    }

    [Theory]
    [InlineData(1, "2025551234", "+1202-555-1234")] // US - formatted by libphonenumber
    [InlineData(44, "2079460958", "+442079460958")] // UK
    [InlineData(20, "1001234567", "+201001234567")] // Egypt
    public void should_normalize_phone_number(int countryCode, string number, string expectedNormalized)
    {
        // given
        var phoneNumber = new PhoneNumber(countryCode, number);

        // when
        var result = phoneNumber.Normalize();

        // then
        result.Should().Be(expectedNormalized);
    }

    [Fact]
    public void should_convert_to_utils_phone_number()
    {
        // given
        var phoneNumber = new PhoneNumber(1, "5555555555");

        // when
        var utilsPhoneNumber = phoneNumber.ToUtilsPhoneNumber();

        // then
        utilsPhoneNumber.Should().NotBeNull();
        utilsPhoneNumber.CountryCode.Should().Be(1);
        utilsPhoneNumber.NationalNumber.Should().Be(5555555555);
    }

    [Theory]
    [InlineData("+1-555-555-5555", 1, "5555555555")] // US
    [InlineData("+44 20 7946 0958", 44, "2079460958")] // UK
    [InlineData("+20 100 123 4567", 20, "1001234567")] // Egypt
    public void should_create_from_international_format(string internationalNumber, int expectedCountryCode, string expectedNumber)
    {
        // when
        var phoneNumber = PhoneNumber.FromInternationalFormat(internationalNumber);

        // then
        phoneNumber.Should().NotBeNull();
        phoneNumber!.CountryCode.Should().Be(expectedCountryCode);
        phoneNumber.Number.Should().Be(expectedNumber);
    }

    [Fact]
    public void should_return_null_when_input_is_null()
    {
        // when
        var fromInternational = PhoneNumber.FromInternationalFormat(null);
        var fromPhoneNumber = PhoneNumber.FromPhoneNumber(null);
        var getNationalFormat = PhoneNumber.GetNationalFormat(null);
        var getInternationalFormat = PhoneNumber.GetInternationalFormat(null);

        // then
        fromInternational.Should().BeNull();
        fromPhoneNumber.Should().BeNull();
        getNationalFormat.Should().BeNull();
        getInternationalFormat.Should().BeNull();
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
        (phoneNumber1 == phoneNumber2).Should().BeTrue();
        (phoneNumber1 == phoneNumber3).Should().BeFalse();
        (phoneNumber1 != phoneNumber3).Should().BeTrue();
    }
}
