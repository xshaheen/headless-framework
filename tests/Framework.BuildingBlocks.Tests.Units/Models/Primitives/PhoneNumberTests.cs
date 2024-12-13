// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Primitives;

namespace Tests.Models.Primitives;

public sealed class PhoneNumberTests
{
    [Fact]
    public void phone_number_should_be_correctly_initialized()
    {
        // given
        int countryCode = 20;
        string number = "1018541323";

        // when
        var phoneNumber = new PhoneNumber(countryCode, number);

        // then
        phoneNumber.CountryCode.Should().Be(20);
        phoneNumber.Number.Should().Be("1018541323");
        phoneNumber.ToString().Should().Be("(20) 1018541323");
    }

    [Fact]
    public void normalize_should_return_correct_format()
    {
        // given
        int countryCode = 20;
        string number = "1018541323";

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

    [Fact]
    public void normalize_single_string_should_return_correct_format()
    {
        // given
        const string number = " 010 18541 323";

        // when
        var normalized = PhoneNumber.Normalize(number);

        // then
        normalized.Should().Be("01018541323");
    }

    [Fact]
    public void from_international_format_should_return_valid_phone_number()
    {
        // given
        string internationalNumber = "+20 101-854-1323";

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
}
