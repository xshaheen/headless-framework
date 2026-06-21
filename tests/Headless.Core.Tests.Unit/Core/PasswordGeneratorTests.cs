// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;

namespace Tests.Core;

public sealed class PasswordGeneratorTests
{
    private readonly PasswordGenerator _passwordGenerator = new();

    [Fact]
    public void generate_password_should_return_password_of_specified_length()
    {
        // given
        const int length = 12;

        // when
        var password = _passwordGenerator.GeneratePassword(new GeneratePasswordOptions(length));

        // then
        password.Should().HaveLength(length);
    }

    [Fact]
    public void generate_password_should_contain_required_unique_characters()
    {
        // given
        const int length = 10;
        const int requiredUniqueChars = 10;

        // when
        var password = _passwordGenerator.GeneratePassword(
            new GeneratePasswordOptions(length) { RequiredUniqueChars = requiredUniqueChars }
        );

        // then
        password.Distinct().Should().HaveCountGreaterThanOrEqualTo(requiredUniqueChars);
    }

    [Fact]
    public void generate_password_should_throw_if_invalid_configuration()
    {
        // when
        Action act = () =>
            _passwordGenerator.GeneratePassword(
                new GeneratePasswordOptions(10)
                {
                    UseDigitsInRemaining = false,
                    UseLowercaseInRemaining = false,
                    UseUppercaseInRemaining = false,
                    UseNonAlphanumericInRemaining = false,
                }
            );

        // then
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                "Invalid password configuration provided. At least one character set must be used in remaining characters."
            );
    }

    [Fact]
    public void validate_required_unique_chars_should_throw_invalid_operation_exception()
    {
        // when
        Action action = () =>
            _passwordGenerator.GeneratePassword(new GeneratePasswordOptions(12) { RequiredUniqueChars = 200 });

        // then
        action
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                "Invalid password configuration provided. Required unique characters count is greater than the total available characters."
            );
    }

    [Fact]
    public void generate_password_should_throw_argument_out_of_range_exception()
    {
        // given
        const int length = -1;

        // when
        Action action = () => _passwordGenerator.GeneratePassword(new GeneratePasswordOptions(length));

        // then
        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void generate_password_should_throw_when_length_is_smaller_than_required_character_sets()
    {
        // when (defaults require digit + lowercase + uppercase + non-alphanumeric => 4 required sets)
        Action action = () => _passwordGenerator.GeneratePassword(new GeneratePasswordOptions(2));

        // then
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void generate_password_should_return_exactly_the_requested_length()
    {
        // when
        var password = _passwordGenerator.GeneratePassword(new GeneratePasswordOptions(6));

        // then
        password.Should().HaveLength(6);
    }

    [Fact]
    public void generate_password_should_not_crash_when_required_unique_chars_exceed_enabled_remaining_set()
    {
        // when (only digits are enabled for the "remaining" pool, so fewer than 20 distinct chars exist)
        Action action = () =>
            _passwordGenerator.GeneratePassword(new GeneratePasswordOptions(20) { RequiredUniqueChars = 20 });

        // then
        action.Should().NotThrow();
    }

    [Fact]
    public void generate_password_with_defaults_should_contain_each_required_character_set()
    {
        // given — defaults require a digit, lowercase, uppercase, and non-alphanumeric character
        var password = _passwordGenerator.GeneratePassword(new GeneratePasswordOptions(16));

        // then
        password.Should().HaveLength(16);
        password.Any(char.IsDigit).Should().BeTrue("a digit is required by default");
        password.Any(char.IsLower).Should().BeTrue("a lowercase letter is required by default");
        password.Any(char.IsUpper).Should().BeTrue("an uppercase letter is required by default");
        password.Any(c => !char.IsLetterOrDigit(c)).Should().BeTrue("a non-alphanumeric is required by default");
    }
}
