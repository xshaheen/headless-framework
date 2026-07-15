// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;

namespace Tests.Primitives;

public sealed class UserIdTests
{
    [Fact]
    public void should_return_false_for_null_without_throwing_when_try_create()
    {
        // when - the bug: `value.Length` NRE'd on null instead of returning false
        var success = UserId.TryCreate(null!, out var result);

        // then
        success.Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact]
    public void should_return_false_with_error_message_for_null_when_try_create()
    {
        // when
        var success = UserId.TryCreate(null!, out var result, out var errorMessage);

        // then
        success.Should().BeFalse();
        result.Should().BeNull();
        errorMessage.Should().NotBeNull();
    }

    [Fact]
    public void should_return_false_for_empty_when_try_create()
    {
        // when
        var success = UserId.TryCreate("", out var result);

        // then
        success.Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact]
    public void should_succeed_for_non_empty_when_try_create()
    {
        // when
        var success = UserId.TryCreate("user-1234", out var result);

        // then
        success.Should().BeTrue();
        result.Should().NotBeNull();
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("u", true)]
    public void should_be_null_safe_when_validate(string? value, bool expectedValid)
    {
        // when
        var result = UserId.Validate(value!);

        // then
        result.IsValid.Should().Be(expectedValid);
    }
}
