// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using Headless.Serializer;

namespace Tests;

public sealed class JsonCanonicalizerTests
{
    private static readonly string _VectorsDirectory = Path.Combine(AppContext.BaseDirectory, "TestData", "Jcs");

    public static TheoryData<string> ReferenceVectors =>
        ["arrays", "french", "structures", "unicode", "values", "weird"];

    [Theory]
    [MemberData(nameof(ReferenceVectors))]
    public void should_match_reference_output_when_canonicalizing_reference_input(string name)
    {
        var input = File.ReadAllBytes(Path.Combine(_VectorsDirectory, "input", name + ".json"));
        var expected = File.ReadAllBytes(Path.Combine(_VectorsDirectory, "output", name + ".json"));

        JsonCanonicalizer.Canonicalize(input).Should().Equal(expected);

        using var document = JsonDocument.Parse(input);
        JsonCanonicalizer.Canonicalize(document.RootElement).Should().Equal(expected);
    }

    [Fact]
    public void should_write_ecmascript_number_form_when_canonicalizing_generated_doubles()
    {
        var failures = new List<string>();

        foreach (var line in File.ReadLines(Path.Combine(_VectorsDirectory, "numbers.csv")))
        {
            var separator = line.IndexOf(',', StringComparison.Ordinal);
            var literal = line[..separator];
            var expected = line[(separator + 1)..];
            var actual = _Canonical(literal);

            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                failures.Add($"{literal}: expected {expected}, got {actual}");
            }
        }

        failures.Should().BeEmpty();
    }

    [Theory]
    [InlineData("-0", "0")]
    [InlineData("-0.0", "0")]
    [InlineData("0e10", "0")]
    [InlineData("1.0", "1")]
    [InlineData("1e2", "100")]
    [InlineData("1E+2", "100")]
    [InlineData("-1.5e-7", "-1.5e-7")]
    [InlineData("0.000001", "0.000001")]
    [InlineData("1e21", "1e+21")]
    [InlineData("1e20", "100000000000000000000")]
    [InlineData("1e-400", "0")]
    [InlineData("0.10000000000000001", "0.1")]
    [InlineData("333333333.33333329", "333333333.3333333")]
    [InlineData("9007199254740991", "9007199254740991")]
    [InlineData("-9007199254740991", "-9007199254740991")]
    [InlineData("9007199254740993.0", "9007199254740992")]
    public void should_write_canonical_number_when_literal_is_representable(string literal, string expected)
    {
        _Canonical(literal).Should().Be(expected);
    }

    [Theory]
    [InlineData("9007199254740992")]
    [InlineData("9007199254740993")]
    [InlineData("-9007199254740992")]
    [InlineData("12345678901234567890")]
    public void should_throw_when_integer_literal_is_outside_safe_range(string literal)
    {
        var act = () => JsonCanonicalizer.Canonicalize(Encoding.UTF8.GetBytes($$"""{"id":{{literal}}}"""));

        act.Should().ThrowExactly<JsonException>().Which.Path.Should().Be("$['id']");
    }

    [Theory]
    [InlineData("1e400")]
    [InlineData("-1e400")]
    public void should_throw_when_number_is_outside_double_range(string literal)
    {
        var act = () => JsonCanonicalizer.Canonicalize(Encoding.UTF8.GetBytes(literal));

        act.Should().ThrowExactly<JsonException>();
    }

    [Fact]
    public void should_produce_same_output_when_member_order_and_whitespace_differ()
    {
        var first = """{"b":1,"a":[1,{"d":2,"c":3}],"e":{"g":true,"f":null}}"""u8;
        var second =
            """
            {
              "e": { "f": null, "g": true },
              "a": [ 1, { "c": 3, "d": 2 } ],
              "b": 1
            }
            """u8;

        _Canonical(first).Should().Be("""{"a":[1,{"c":3,"d":2}],"b":1,"e":{"f":null,"g":true}}""");
        JsonCanonicalizer.Hash(first).Should().Equal(JsonCanonicalizer.Hash(second));
    }

    [Fact]
    public void should_keep_array_order_when_arrays_are_nested()
    {
        _Canonical("""[ [3, [2, [ ]]], { }, [ {"b":[1,0],"a":[]} ] ]""")
            .Should()
            .Be("""[[3,[2,[]]],{},[{"a":[],"b":[1,0]}]]""");
    }

    [Fact]
    public void should_escape_only_quote_backslash_and_control_characters_when_writing_strings()
    {
        const string input = """
            "éA\u001f\u007f\u2028😀<+`\"\\\/\b\f\n\r\t\u0000"
            """;

        const string expected = "\"éA\\u001f\u007f\u2028😀<+`\\\"\\\\/\\b\\f\\n\\r\\t\\u0000\"";

        _Canonical(input).Should().Be(expected);
    }

    [Fact]
    public void should_sort_members_by_utf16_code_units_when_names_mix_planes()
    {
        // UTF-16 puts the surrogate pair (0xD83D) before U+E000; UTF-8 byte order would reverse them.
        _Canonical("""{"\ue000":1,"\ud83d\ude00":2,"a":3}""").Should().Be("{\"a\":3,\"\U0001F600\":2,\"\uE000\":1}");
    }

    [Fact]
    public void should_throw_when_span_input_has_duplicate_members()
    {
        var act = () => JsonCanonicalizer.Canonicalize("""{"a":1,"a":2}"""u8);

        act.Should().ThrowExactly<JsonException>();
    }

    [Fact]
    public void should_throw_with_path_when_element_has_duplicate_members()
    {
        // JsonDocument accepts duplicates by default, so the canonicalizer has to catch them itself.
        using var document = JsonDocument.Parse("""{"a":[0,{"x":1,"y":2,"x":3}]}""");

        var act = () => JsonCanonicalizer.Canonicalize(document.RootElement);

        act.Should().ThrowExactly<JsonException>().Which.Path.Should().Be("$['a'][1]");
    }

    [Theory]
    [InlineData(
        """
            "\ud800"
            """
    )]
    [InlineData(
        """
            {"\udc00":1}
            """
    )]
    public void should_throw_when_string_has_lone_surrogate(string input)
    {
        var act = () => JsonCanonicalizer.Canonicalize(Encoding.UTF8.GetBytes(input));

        act.Should().ThrowExactly<JsonException>();
    }

    [Theory]
    [InlineData("{} x")]
    [InlineData("{} {}")]
    [InlineData("[1,]")]
    [InlineData("/* c */ 1")]
    [InlineData("")]
    public void should_throw_when_span_input_is_not_one_strict_json_value(string input)
    {
        var act = () => JsonCanonicalizer.Canonicalize(Encoding.UTF8.GetBytes(input));

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void should_throw_when_element_is_default()
    {
        var act = () => JsonCanonicalizer.Canonicalize(default(JsonElement));

        act.Should().ThrowExactly<ArgumentException>();
    }

    [Fact]
    public void should_hash_canonical_bytes_with_sha256_when_hashing()
    {
        var json = """{ "b": "é", "a": 1.0 }"""u8;
        var expected = SHA256.HashData(JsonCanonicalizer.Canonicalize(json));

        JsonCanonicalizer.Hash(json).Should().Equal(expected);

        using var document = JsonDocument.Parse(json.ToArray());
        JsonCanonicalizer.Hash(document.RootElement).Should().Equal(expected);

        Span<byte> destination = stackalloc byte[JsonCanonicalizer.HashSizeInBytes + 4];
        JsonCanonicalizer.Hash(json, destination);
        destination[..JsonCanonicalizer.HashSizeInBytes].ToArray().Should().Equal(expected);
    }

    [Fact]
    public void should_throw_when_hash_destination_is_too_short()
    {
        var act = () => JsonCanonicalizer.Hash("{}"u8, new byte[JsonCanonicalizer.HashSizeInBytes - 1]);

        act.Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void should_append_to_buffer_writer_when_canonicalizing_into_one()
    {
        var output = new ArrayBufferWriter<byte>();
        output.Write("prefix:"u8);

        JsonCanonicalizer.Canonicalize("""{"b":2,"a":1}"""u8, output);

        Encoding.UTF8.GetString(output.WrittenSpan).Should().Be("""prefix:{"a":1,"b":2}""");
    }

    private static string _Canonical(string json)
    {
        return _Canonical(Encoding.UTF8.GetBytes(json));
    }

    private static string _Canonical(ReadOnlySpan<byte> json)
    {
        return Encoding.UTF8.GetString(JsonCanonicalizer.Canonicalize(json));
    }
}
