// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;

namespace Tests.Binding;

/// <summary>
/// Pins that the pre-binding check accepts and rejects the same text as the parse the request delegate factory applies,
/// with the styles it passes, so the check never answers 400 for a value the factory would bind.
/// </summary>
public sealed class SimpleValueParserTests : TestBase
{
    [Theory]
    [InlineData(typeof(int), "42", true)]
    [InlineData(typeof(int), "1,000", false)]
    [InlineData(typeof(decimal), "1,000.5", true)]
    [InlineData(typeof(double), "1e3", true)]
    [InlineData(typeof(Guid), "0b6b5c0e-4f0e-4d4e-9a52-3c1f0f0b8c11", true)]
    [InlineData(typeof(Guid), "nope", false)]
    [InlineData(typeof(DateTime), " 2026-01-02T03:04:05Z ", true)]
    [InlineData(typeof(DateTime), "nope", false)]
    [InlineData(typeof(DateTimeOffset), " 2026-01-02T03:04:05+02:00 ", true)]
    [InlineData(typeof(DateOnly), " 2026-01-02 ", true)]
    [InlineData(typeof(DateOnly), "2026-13-40", false)]
    [InlineData(typeof(TimeOnly), " 13:45 ", true)]
    [InlineData(typeof(Uri), "/relative/path", true)]
    [InlineData(typeof(ProviderParsed), "ok", true)]
    [InlineData(typeof(ProviderParsed), "no", false)]
    [InlineData(typeof(PlainParsed), "ok", true)]
    [InlineData(typeof(PlainParsed), "no", false)]
    public void should_accept_exactly_what_the_factory_parse_accepts(Type type, string value, bool expected)
    {
        var parse = SimpleValueParser.For(type);

        parse.Should().NotBeNull();
        parse!(value).Should().Be(expected);
    }

    [Theory]
    [InlineData(typeof(string))]
    [InlineData(typeof(BoundAsync))]
    [InlineData(typeof(object))]
    public void should_leave_a_type_the_factory_does_not_parse_with_try_parse_unchecked(Type type)
    {
        SimpleValueParser.For(type).Should().BeNull();
    }

#pragma warning disable IDE0060 // False positive: these are the exact TryParse and BindAsync shapes the parser looks for, so each parameter stays even where the stub ignores it.
    public sealed class ProviderParsed
    {
        public static bool TryParse(string? value, IFormatProvider? provider, out ProviderParsed result)
        {
            result = new ProviderParsed();

            return string.Equals(value, "ok", StringComparison.Ordinal);
        }
    }

    public sealed class PlainParsed
    {
        public static bool TryParse(string? value, out PlainParsed result)
        {
            result = new PlainParsed();

            return string.Equals(value, "ok", StringComparison.Ordinal);
        }
    }

    public sealed class BoundAsync
    {
        public static bool TryParse(string? value, out BoundAsync result)
        {
            result = new BoundAsync();

            return true;
        }

        public static ValueTask<BoundAsync?> BindAsync(HttpContext context) => ValueTask.FromResult<BoundAsync?>(null);
    }
#pragma warning restore IDE0060
}
