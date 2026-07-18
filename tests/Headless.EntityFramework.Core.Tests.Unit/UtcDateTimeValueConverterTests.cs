// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.EntityFramework.Configurations;
using Headless.Testing.Tests;

namespace Tests;

public sealed class UtcDateTimeValueConverterTests : TestBase
{
    [Fact]
    public void should_preserve_utc_value()
    {
        var value = new DateTime(2026, 7, 18, 10, 30, 0, DateTimeKind.Utc);

        var result = _Convert(new UtcDateTimeValueConverter(), value);

        result.Should().Be(value);
        result.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void should_convert_local_value_to_utc()
    {
        var value = new DateTime(2026, 7, 18, 10, 30, 0, DateTimeKind.Local);

        var result = _Convert(new UtcDateTimeValueConverter(), value);

        result.Should().Be(value.ToUniversalTime());
        result.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void should_stamp_unspecified_value_as_utc_without_shifting_clock_value()
    {
        var value = new DateTime(2026, 7, 18, 10, 30, 0, DateTimeKind.Unspecified);

        var result = _Convert(new UtcDateTimeValueConverter(), value);

        result.Ticks.Should().Be(value.Ticks);
        result.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void should_preserve_null_nullable_value()
    {
        var converter = new NullableUtcDateTimeValueConverter();
        var convert = converter.ConvertToProviderExpression.Compile();

        var result = convert(null);

        result.Should().BeNull();
    }

    [Fact]
    public void should_normalize_nullable_value()
    {
        var value = new DateTime(2026, 7, 18, 10, 30, 0, DateTimeKind.Unspecified);
        var converter = new NullableUtcDateTimeValueConverter();
        var convert = converter.ConvertFromProviderExpression.Compile();

        var result = convert(value);

        result.Should().NotBeNull();
        result.Value.Ticks.Should().Be(value.Ticks);
        result.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    private static DateTime _Convert(UtcDateTimeValueConverter converter, DateTime value)
    {
        var toProvider = converter.ConvertToProviderExpression.Compile();
        var fromProvider = converter.ConvertFromProviderExpression.Compile();

        return fromProvider(toProvider(value));
    }
}
