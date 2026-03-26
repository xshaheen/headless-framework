// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Orm.EntityFramework.Configurations;
using Headless.Primitives;

namespace Tests.Configurations;

public sealed class PrimitiveValueConverterTests
{
    [Fact]
    public void user_id_converter_should_convert_to_provider()
    {
        // given
        var converter = new UserIdValueConverter();
        var userId = new UserId("user-123");

        // when
        var result = converter.ConvertToProvider(userId);

        // then
        result.Should().Be("user-123");
    }

    [Fact]
    public void user_id_converter_should_convert_from_provider()
    {
        // given
        var converter = new UserIdValueConverter();

        // when
        var result = converter.ConvertFromProvider("user-456");

        // then
        result.Should().BeOfType<UserId>();
        ((UserId)result!).GetUnderlyingPrimitiveValue().Should().Be("user-456");
    }

    [Fact]
    public void user_id_converter_should_roundtrip()
    {
        // given
        var converter = new UserIdValueConverter();
        var original = new UserId("user-rt");

        // when
        var stored = converter.ConvertToProvider(original);
        var restored = converter.ConvertFromProvider(stored);

        // then
        ((UserId)restored!)
            .GetUnderlyingPrimitiveValue()
            .Should()
            .Be("user-rt");
    }

    [Fact]
    public void user_id_compiled_from_provider_expression_should_produce_valid_primitive()
    {
        // Compiles the expression tree the same way EF Core's materializer
        // does — the exact code path that broke with the old (TPrimitive)(object)v cast.
        var converter = new UserIdValueConverter();
        var compiled = converter.ConvertFromProviderExpression.Compile();

        // when
        var result = compiled("compiled-test");

        // then
        result.Should().BeOfType<UserId>();
        ((UserId)result!).GetUnderlyingPrimitiveValue().Should().Be("compiled-test");
    }

    [Fact]
    public void account_id_converter_should_convert_to_provider()
    {
        // given
        var converter = new AccountIdValueConverter();
        var accountId = new AccountId("acc-123");

        // when
        var result = converter.ConvertToProvider(accountId);

        // then
        result.Should().Be("acc-123");
    }

    [Fact]
    public void account_id_converter_should_convert_from_provider()
    {
        // given
        var converter = new AccountIdValueConverter();

        // when
        var result = converter.ConvertFromProvider("acc-456");

        // then
        result.Should().BeOfType<AccountId>();
        ((AccountId)result!).GetUnderlyingPrimitiveValue().Should().Be("acc-456");
    }

    [Fact]
    public void account_id_compiled_from_provider_expression_should_produce_valid_primitive()
    {
        var converter = new AccountIdValueConverter();
        var compiled = converter.ConvertFromProviderExpression.Compile();

        // when
        var result = compiled("compiled-acct");

        // then
        result.Should().BeOfType<AccountId>();
        ((AccountId)result!).GetUnderlyingPrimitiveValue().Should().Be("compiled-acct");
    }
}
