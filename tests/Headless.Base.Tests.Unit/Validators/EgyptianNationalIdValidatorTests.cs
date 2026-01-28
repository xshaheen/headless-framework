// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Validators;

namespace Tests.Validators;

public sealed class EgyptianNationalIdValidatorTests
{
    // Valid ID structure: CYYMMDDGGSSSSR
    // C = Century (2=1900s, 3=2000s)
    // YY = Year, MM = Month, DD = Day
    // GG = Governorate code, SSSS = Serial, R = Check digit

    [Theory]
    [InlineData("29001011234567")] // 1990-01-01, Cairo (01)
    [InlineData("30001012234567")] // 2000-01-01, Alexandria (02)
    [InlineData("29512311112345")] // 1995-12-31, Damietta (11)
    [InlineData("30206152112345")] // 2002-06-15, Giza (21)
    public void should_return_true_for_valid_id(string nationalId)
    {
        var result = EgyptianNationalIdValidator.IsValid(nationalId);
        result.Should().BeTrue();
    }

    [Fact]
    public void should_return_false_for_null()
    {
        var result = EgyptianNationalIdValidator.IsValid(null!);
        result.Should().BeFalse();
    }

    [Fact]
    public void should_return_false_for_empty()
    {
        var result = EgyptianNationalIdValidator.IsValid("");
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("1234567890123")] // 13 digits
    [InlineData("123456789012345")] // 15 digits
    [InlineData("12345")] // too short
    public void should_return_false_for_wrong_length(string nationalId)
    {
        var result = EgyptianNationalIdValidator.IsValid(nationalId);
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("2900101123456a")]
    [InlineData("abcdefghijklmn")]
    [InlineData("29001011234-67")]
    public void should_return_false_for_non_numeric(string nationalId)
    {
        var result = EgyptianNationalIdValidator.IsValid(nationalId);
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("29013321234567")] // Invalid day 33
    [InlineData("29001321234567")] // Invalid month 13
    [InlineData("29002291234567")] // Feb 29 in non-leap year (1990)
    [InlineData("29000001234567")] // Invalid month 00
    [InlineData("29001001234567")] // Invalid day 00
    public void should_return_false_for_invalid_date(string nationalId)
    {
        var result = EgyptianNationalIdValidator.IsValid(nationalId);
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("29001010012345")] // Governorate code 00 - invalid
    [InlineData("29001019912345")] // Governorate code 99 - invalid
    [InlineData("29001015012345")] // Governorate code 50 - invalid
    public void should_return_false_for_invalid_governorate(string nationalId)
    {
        var result = EgyptianNationalIdValidator.IsValid(nationalId);
        result.Should().BeFalse();
    }

    [Fact]
    public void should_parse_valid_id_correctly()
    {
        // 29501011112345 = 1995-01-01, Damietta (11)
        var result = EgyptianNationalIdValidator.TryParse(
            "29501011112345",
            out var year,
            out var month,
            out var day,
            out var governorate
        );

        result.Should().BeTrue();
        year.Should().Be(1995);
        month.Should().Be(1);
        day.Should().Be(1);
        governorate.Should().Be("دمياط");
    }

    [Fact]
    public void should_parse_2000s_id_correctly()
    {
        // 30001012112345 = 2000-01-01, Giza (21)
        var result = EgyptianNationalIdValidator.TryParse(
            "30001012112345",
            out var year,
            out var month,
            out var day,
            out var governorate
        );

        result.Should().BeTrue();
        year.Should().Be(2000);
        month.Should().Be(1);
        day.Should().Be(1);
        governorate.Should().Be("الجيزة");
    }

    [Fact]
    public void should_return_false_from_TryParse_for_invalid()
    {
        var result = EgyptianNationalIdValidator.TryParse(
            "invalid",
            out var year,
            out var month,
            out var day,
            out var governorate
        );

        result.Should().BeFalse();
        year.Should().Be(0);
        month.Should().Be(0);
        day.Should().Be(0);
        governorate.Should().BeEmpty();
    }

    [Fact]
    public void should_expose_governorate_map()
    {
        var map = EgyptianNationalIdValidator.GovernorateIdMap;

        map.Should().NotBeEmpty();
        map.Should().ContainKey("01"); // Cairo
        map.Should().ContainKey("02"); // Alexandria
        map.Should().ContainKey("21"); // Giza
        map["01"].Should().Be("القاهرة");
    }

    [Fact]
    public void governorate_map_should_contain_all_valid_codes()
    {
        var map = EgyptianNationalIdValidator.GovernorateIdMap;

        // Verify key governorates
        map["01"].Should().Be("القاهرة");
        map["02"].Should().Be("آلإسكندرية");
        map["03"].Should().Be("بور سعيد");
        map["04"].Should().Be("السويس");
        map["11"].Should().Be("دمياط");
        map["21"].Should().Be("الجيزة");
        map["88"].Should().Be("N/A");
    }
}
