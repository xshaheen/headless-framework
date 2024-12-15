// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.BuildingBlocks.Helpers.Normalizers;
using Tests.Fakers;

namespace Tests.Helpers.Normalizers;

public class LookupNormalizerExtensionsTests
{
    [Fact]
    public void normalize_name_should_return_uppercase_trimmed_name()
    {
        // given
        const string input = "  John Doe  ";
        const string expected = "JOHN DOE";

        // when
        var result = input.NormalizeName();

        // then
        result.Should().Be(expected);
    }

    [Fact]
    public void normalize_name_should_return_null_when_input_is_null()
    {
        // given
        const string? input = null;

        // when
        var result = input.NormalizeName();

        // then
        result.Should().BeNull();
    }

    [Fact]
    public void normalize_email_should_return_normalized_name_for_email()
    {
        // given
        var email = FakerData.GenerateEmail();
        var expected = email.ToUpper(CultureInfo.CurrentCulture);

        // when
        var result = email.NormalizeEmail();

        // then
        result.Should().Be(expected);
    }

    [Fact]
    public void normalize_email_should_return_null_when_email_is_null()
    {
        // given
        const string? email = null;

        // when
        var result = email.NormalizeEmail();

        // then
        result.Should().BeNull();
    }

    // Bug here because "\0"
    [Fact]
    public void normalize_phone_number_should_remove_spaces_and_return_invariant_digits()
    {
        // given
        const string phoneNumber = " 123 456 789 ";
        const string expected = "123456789";

        // when
        var result = phoneNumber.NormalizePhoneNumber();

        // then
        result.Should().Be(expected);
    }

    [Fact]
    public void normalize_phone_number_should_return_null_when_input_is_null()
    {
        // given
        const string? phoneNumber = null;

        // when
        var result = phoneNumber.NormalizePhoneNumber();

        // then
        result.Should().BeNull();
    }
}
