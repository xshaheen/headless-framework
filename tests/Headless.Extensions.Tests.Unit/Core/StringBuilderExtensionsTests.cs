// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Core;

namespace Tests.Core;

public sealed class StringBuilderExtensionsTests
{
    private static string _Invariant<T>(T value)
        where T : IFormattable
    {
        return value.ToString(format: null, CultureInfo.InvariantCulture);
    }

    [Fact]
    public void should_match_invariant_for_integer_boundaries_when_append_invariant()
    {
        new StringBuilder().AppendInvariant(byte.MinValue).ToString().Should().Be(_Invariant(byte.MinValue));
        new StringBuilder().AppendInvariant(byte.MaxValue).ToString().Should().Be(_Invariant(byte.MaxValue));
        new StringBuilder().AppendInvariant(sbyte.MinValue).ToString().Should().Be(_Invariant(sbyte.MinValue));
        new StringBuilder().AppendInvariant(sbyte.MaxValue).ToString().Should().Be(_Invariant(sbyte.MaxValue));
        new StringBuilder().AppendInvariant(short.MinValue).ToString().Should().Be(_Invariant(short.MinValue));
        new StringBuilder().AppendInvariant(short.MaxValue).ToString().Should().Be(_Invariant(short.MaxValue));
        new StringBuilder().AppendInvariant(ushort.MaxValue).ToString().Should().Be(_Invariant(ushort.MaxValue));
        new StringBuilder().AppendInvariant(int.MinValue).ToString().Should().Be(_Invariant(int.MinValue));
        new StringBuilder().AppendInvariant(int.MaxValue).ToString().Should().Be(_Invariant(int.MaxValue));
        new StringBuilder().AppendInvariant(uint.MaxValue).ToString().Should().Be(_Invariant(uint.MaxValue));
        new StringBuilder().AppendInvariant(long.MinValue).ToString().Should().Be(_Invariant(long.MinValue));
        new StringBuilder().AppendInvariant(long.MaxValue).ToString().Should().Be(_Invariant(long.MaxValue));
        new StringBuilder().AppendInvariant(ulong.MaxValue).ToString().Should().Be(_Invariant(ulong.MaxValue));
    }

    [Fact]
    public void should_match_invariant_for_floating_and_decimal_boundaries_when_append_invariant()
    {
        new StringBuilder().AppendInvariant(Half.MaxValue).ToString().Should().Be(_Invariant(Half.MaxValue));
        new StringBuilder().AppendInvariant(float.MinValue).ToString().Should().Be(_Invariant(float.MinValue));
        new StringBuilder().AppendInvariant(float.MaxValue).ToString().Should().Be(_Invariant(float.MaxValue));
        new StringBuilder().AppendInvariant(123.456f).ToString().Should().Be(_Invariant(123.456f));
        new StringBuilder().AppendInvariant(double.MinValue).ToString().Should().Be(_Invariant(double.MinValue));
        new StringBuilder().AppendInvariant(double.MaxValue).ToString().Should().Be(_Invariant(double.MaxValue));
        new StringBuilder().AppendInvariant(1234.5678d).ToString().Should().Be(_Invariant(1234.5678d));

        // decimal.MinValue/MaxValue are ~30 chars and exercise the larger stack buffer.
        new StringBuilder()
            .AppendInvariant(decimal.MinValue)
            .ToString()
            .Should()
            .Be(_Invariant(decimal.MinValue));
        new StringBuilder().AppendInvariant(decimal.MaxValue).ToString().Should().Be(_Invariant(decimal.MaxValue));
        new StringBuilder().AppendInvariant(1234.5m).ToString().Should().Be(_Invariant(1234.5m));
    }

    [Fact]
    public void should_use_invariant_culture_under_a_non_invariant_culture_when_append_invariant()
    {
        using (CultureHelper.Use("de-DE")) // German formats decimals with ',' and groups with '.'
        {
            new StringBuilder().AppendInvariant(1234.5d).ToString().Should().Be("1234.5");
            new StringBuilder().AppendInvariant(1234.5m).ToString().Should().Be("1234.5");
            new StringBuilder().AppendInvariant(1000000).ToString().Should().Be("1000000");
        }
    }

    [Fact]
    public void should_append_nothing_when_append_invariant_nullable_null()
    {
        new StringBuilder().AppendInvariant((int?)null).ToString().Should().Be("");
        new StringBuilder().AppendInvariant((double?)null).ToString().Should().Be("");
        new StringBuilder().AppendInvariant((decimal?)null).ToString().Should().Be("");
    }

    [Fact]
    public void should_append_value_when_append_invariant_nullable_present()
    {
        new StringBuilder().AppendInvariant((int?)42).ToString().Should().Be("42");
        new StringBuilder().AppendInvariant((long?)long.MaxValue).ToString().Should().Be(_Invariant(long.MaxValue));
        new StringBuilder().AppendInvariant((decimal?)123.45m).ToString().Should().Be("123.45");
    }

    [Fact]
    public void should_chain_multiple_appends_when_append_invariant()
    {
        var result = new StringBuilder().AppendInvariant(1).Append('-').AppendInvariant(2.5d).ToString();

        result.Should().Be("1-2.5");
    }

    [Fact]
    public void should_use_span_formattable_fast_path_when_append_invariant_generic()
    {
        var guid = Guid.NewGuid();
        var expected = guid.ToString(null, CultureInfo.InvariantCulture);

        // Guid is ISpanFormattable, so it takes the span fast path; output must equal the invariant ToString.
        new StringBuilder()
            .AppendInvariant(guid)
            .ToString()
            .Should()
            .Be(expected);
    }

    [Fact]
    public void should_fall_back_for_formattable_only_values_when_append_invariant_generic()
    {
        // PlainFormattable implements IFormattable but not ISpanFormattable, exercising the ToString fallback.
        new StringBuilder()
            .AppendInvariant(new PlainFormattable(7))
            .ToString()
            .Should()
            .Be("PF(7)");
    }

    [Fact]
    public void should_format_with_invariant_culture_when_append_invariant_formattable_string()
    {
        using (CultureHelper.Use("de-DE"))
        {
            FormattableString value = $"{1234.5d}";

            new StringBuilder().AppendInvariant(value).ToString().Should().Be("1234.5");
        }
    }

    private sealed class PlainFormattable(int value) : IFormattable
    {
        public string ToString(string? format, IFormatProvider? formatProvider)
        {
            return $"PF({value.ToString(CultureInfo.InvariantCulture)})";
        }
    }
}
