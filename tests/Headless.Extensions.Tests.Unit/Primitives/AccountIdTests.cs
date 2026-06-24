// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Tests.Primitives;

public sealed class AccountIdTests
{
    [Fact]
    public void try_create_should_return_false_for_null_without_throwing()
    {
        // when - the bug: `value.Length` NRE'd on null instead of returning false
        var success = AccountId.TryCreate(null!, out var result);

        // then
        success.Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact]
    public void try_create_should_return_false_with_error_message_for_null()
    {
        // when
        var success = AccountId.TryCreate(null!, out var result, out var errorMessage);

        // then
        success.Should().BeFalse();
        result.Should().BeNull();
        errorMessage.Should().NotBeNull();
    }

    [Fact]
    public void try_create_should_return_false_for_empty()
    {
        // when
        var success = AccountId.TryCreate("", out var result);

        // then
        success.Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact]
    public void try_create_should_succeed_for_non_empty()
    {
        // when
        var success = AccountId.TryCreate("user-1234", out var result);

        // then
        success.Should().BeTrue();
        result.Should().NotBeNull();
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("a", true)]
    public void validate_should_be_null_safe(string? value, bool expectedValid)
    {
        // when
        var result = AccountId.Validate(value!);

        // then
        result.IsValid.Should().Be(expectedValid);
    }
}
