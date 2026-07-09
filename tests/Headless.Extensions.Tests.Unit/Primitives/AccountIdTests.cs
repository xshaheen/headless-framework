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

    [Fact]
    public void json_round_trip_should_preserve_account_id_value()
    {
        // given
        var accountId = new AccountId("user-1234");

        // when
        var json = JsonSerializer.Serialize(accountId);
        var deserialized = JsonSerializer.Deserialize<AccountId>(json);

        // then
        json.Should().Be("\"user-1234\"");
        deserialized.Should().Be(accountId);
    }

    [Fact]
    public void json_deserialize_should_throw_json_exception_for_empty_account_id()
    {
        // when - untrusted input must surface a clean JsonException, not a leaked domain exception
        var act = () => JsonSerializer.Deserialize<AccountId>("\"\"");

        // then
        act.Should().Throw<JsonException>();
    }
}
