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
        var password = _passwordGenerator.GeneratePassword(length);

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
        var password = _passwordGenerator.GeneratePassword(length, requiredUniqueChars);

        // then
        password.Distinct().Should().HaveCountGreaterThanOrEqualTo(requiredUniqueChars);
    }

    [Fact]
    public void generate_password_should_throw_if_invalid_configuration()
    {
        // when
        Action act = () =>
            _passwordGenerator.GeneratePassword(
                10,
                useDigitsInRemaining: false,
                useLowercaseInRemaining: false,
                useUppercaseInRemaining: false,
                useNonAlphanumericInRemaining: false
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
        Action action = () => _passwordGenerator.GeneratePassword(12, 200);

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
        Action action = () => _passwordGenerator.GeneratePassword(length);

        // then
        action.Should().Throw<ArgumentOutOfRangeException>();
    }
}
