// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Core;

namespace Tests.Core;

public sealed class StringExtensionsTests(ITestOutputHelper output) : IDisposable
{
    // U+1F600 GRINNING FACE: a single code point encoded as the surrogate pair 😀 (two UTF-16 chars).
    private const string _Emoji = "😀";

    private readonly IDisposable _cultureScope = CultureHelper.Use("en-US");

    public void Dispose()
    {
        _cultureScope.Dispose();
    }

    [Fact]
    public static void should_allows_null_when_is_null_or_empty()
    {
        var result = ((string?)null).IsNullOrEmpty();
        result.Should().BeTrue();
    }

    [Fact]
    public static void should_allows_null_when_is_null_or_white_space()
    {
        var result = ((string?)null).IsNullOrWhiteSpace();
        result.Should().BeTrue();
    }

    [Fact]
    public void null_if_empty_tests()
    {
        ((string?)null).NullIfEmpty().Should().BeNull();
        "".NullIfEmpty().Should().BeNull();
        "hi".NullIfEmpty().Should().Be("hi");
    }

    [Fact]
    public void null_if_white_space_tests()
    {
        ((string?)null).NullIfWhiteSpace().Should().BeNull();
        "".NullIfWhiteSpace().Should().BeNull();
        "\r\n".NullIfWhiteSpace().Should().BeNull();
        "hi".NullIfWhiteSpace().Should().Be("hi");
    }

    [Fact]
    public void normalize_line_endings_tests()
    {
        const string str = "This\r\n is a\r test \n string";

        var normalized = str.NormalizeLineEndings();
        var lines = normalized.SplitToLines();

        lines.Should().HaveCount(4);
    }

    [Fact]
    public void ensure_ends_with_tests()
    {
        // Expected use-cases
        "Test".EnsureEndsWith('!').Should().Be("Test!");
        "Test!".EnsureEndsWith('!').Should().Be("Test!");
        @"C:\test\folderName".EnsureEndsWith('\\').Should().Be(@"C:\test\folderName\");
        @"C:\test\folderName\".EnsureEndsWith('\\').Should().Be(@"C:\test\folderName\");
        "Sarı".EnsureEndsWith('ı').Should().Be("Sarı");

        // Case differences
        "Egypt".EnsureEndsWith('T').Should().Be("EgyptT");
    }

    [Fact]
    public void ensure_ends_with_culture_specific_tests()
    {
        using (CultureHelper.Use("tr-TR"))
        {
            "Kırmızı".EnsureEndsWith('I', StringComparison.CurrentCultureIgnoreCase).Should().Be("Kırmızı");
        }
    }

    [Fact]
    public void ensure_starts_with_tests()
    {
        // Expected use-cases
        "Test".EnsureStartsWith('~').Should().Be("~Test");
        "~Test".EnsureStartsWith('~').Should().Be("~Test");

        // Case differences
        "Egypt".EnsureStartsWith('t').Should().Be("tEgypt");
    }

    [Fact]
    public void remove_postfix_tests()
    {
        // null case
        const string? nullValue = null;

        nullValue.RemovePrefix(StringComparison.Ordinal, "Test").Should().BeNull();

        // Simple case
        "MyTestAppService".RemovePostfix(StringComparison.Ordinal, "AppService").Should().Be("MyTest");
        "MyTestAppService".RemovePostfix(StringComparison.Ordinal, "Service").Should().Be("MyTestApp");

        // Multiple postfix (orders of postfixes are important)
        "MyTestAppService".RemovePostfix(StringComparison.Ordinal, "AppService", "Service").Should().Be("MyTest");
        "MyTestAppService".RemovePostfix(StringComparison.Ordinal, "Service", "AppService").Should().Be("MyTestApp");

        // Ignore case
        "TestString".RemovePostfix(StringComparison.OrdinalIgnoreCase, "string").Should().Be("Test");

        // Unmatched case
        "MyTestAppService".RemovePostfix(StringComparison.Ordinal, "Unmatched").Should().Be("MyTestAppService");

        // Empty (non-null) input stays empty rather than becoming null (honors [NotNullIfNotNull])
        "".RemovePostfix(StringComparison.Ordinal, "x").Should().Be("");
        "".RemovePostfix('x').Should().Be("");
    }

    [Fact]
    public void remove_prefix_tests()
    {
        "Home.Index".RemovePrefix(StringComparison.Ordinal, "NotMatchedPostfix").Should().Be("Home.Index");
        "Home.About".RemovePrefix(StringComparison.Ordinal, "Home.").Should().Be("About");

        //Ignore case
        "Https://google.com".RemovePrefix(StringComparison.OrdinalIgnoreCase, "https://").Should().Be("google.com");

        // Empty (non-null) input stays empty rather than becoming null
        "".RemovePrefix(StringComparison.Ordinal, "x").Should().Be("");
    }

    [Fact]
    public void truncate_end_tests()
    {
        const string str = "This is a test string";
        const string? nullValue = null;

        str.TruncateEnd(7).Should().Be("This is");
        str.TruncateEnd(0).Should().Be("");
        str.TruncateEnd(100).Should().Be(str);

        nullValue.TruncateEnd(5).Should().Be(null);
    }

    [Fact]
    public void truncate_end_with_postfix_overload_tests()
    {
        const string str = "This is a test string";
        const string? nullValue = null;

        str.TruncateEnd(3, "...").Should().Be("...");
        str.TruncateEnd(12, "...").Should().Be("This is a...");
        str.TruncateEnd(0, "...").Should().Be("");
        str.TruncateEnd(100, "...").Should().Be(str);

        nullValue.TruncateEnd(5).Should().Be(null);

        str.TruncateEnd(3, "~").Should().Be("Th~");
        str.TruncateEnd(12, "~").Should().Be("This is a t~");
        str.TruncateEnd(0, "~").Should().Be("");
        str.TruncateEnd(100, "~").Should().Be(str);

        nullValue.TruncateEnd(5, "~").Should().Be(null);
    }

    [Fact]
    public void one_space_tests()
    {
        "   ".OneSpace().Should().Be(" ");
        "\n\n\n".OneSpace().Should().Be(" ");
        "This\r\n is a\r test \n string".OneSpace().Should().Be("This is a test string");
    }

    [Fact]
    public void nth_index_of_tests()
    {
        const string str = "This is a test string";

        str.NthIndexOf('i', 0).Should().Be(-1);
        str.NthIndexOf('i', 1).Should().Be(2);
        str.NthIndexOf('i', 2).Should().Be(5);
        str.NthIndexOf('i', 3).Should().Be(18);
        str.NthIndexOf('i', 4).Should().Be(-1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("MyStringİ")]
    public void get_bytes_tests(string str)
    {
        var bytes = str.GetBytes();
        bytes.Should().NotBeNull();
        bytes.Should().HaveCountGreaterThanOrEqualTo(str.Length);
        Encoding.UTF8.GetString(bytes).Should().Be(str);
    }

    [Theory]
    [InlineData("")]
    [InlineData("MyString")]
    public void get_bytes_with_encoding_tests(string str)
    {
        var bytes = str.GetBytes(Encoding.ASCII);
        bytes.Should().NotBeNull();
        bytes.Should().HaveCountGreaterThanOrEqualTo(str.Length);
        Encoding.ASCII.GetString(bytes).Should().Be(str);
    }

    [Fact]
    public void to_enum_tests()
    {
        "MyValue1".ToEnum<MyEnum>().Should().Be(MyEnum.MyValue1);
        "MyValue2".ToEnum<MyEnum>().Should().Be(MyEnum.MyValue2);
    }

    private enum MyEnum
    {
        MyValue1,
        MyValue2,
    }

    [Theory]
    // arabic
    [InlineData("آ", "ا")] // Alef With Madda Above
    [InlineData("إ", "ا")] // Alef With Hamza Below
    [InlineData("أ", "ا")] // Alef With Hamza Above
    [InlineData("ء", "ء")]
    [InlineData(" محمود ", " محمود ")]
    [InlineData("يمني", "يمني")] // ي is preserved
    [InlineData("ىمنى", "ىمنى")] // ى is preserved
    [InlineData("شاطئ", "شاطي")]
    [InlineData("لؤ", "لو")]
    [InlineData("بسم الله الرحمن الرحيم", "بسم الله الرحمن الرحيم")]
    [InlineData("بِسْمِ اللَّهِ الرَّحْمَنِ الرَّحِيمِ", "بسم الله الرحمن الرحيم")]
    [InlineData("بِسْمِ اللَّـهِ الرَّحْمَـٰنِ الرَّحِيمِ", "بسم اللـه الرحمـن الرحيم")]
    // latin
    [InlineData("m", "m")]
    [InlineData("123", "123")]
    [InlineData(" Mahmoud 17 ", " Mahmoud 17 ")]
    [InlineData(" crème brûlée", " creme brulee")]
    // white-space
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData(" ", " ")]
    [InlineData("  ", "  ")]
    public void RemoveAccentCharacters_tests(string? value, string? expected)
    {
        // when
        var result = value.RemoveAccentCharacters();

        output.WriteLine($"result   =>{result}");
        output.WriteLine($"expected =>{expected}");

        // then
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("Customer.FirstName", "customer.firstName")]
    [InlineData("Customers[0].FirstName", "customers[0].firstName")]
    [InlineData("OrderDetails.Product.UnitPrice", "orderDetails.product.unitPrice")]
    [InlineData("ID", "iD")]
    [InlineData("XML", "xML")]
    [InlineData("", "")]
    [InlineData("AlreadyCamelCase", "alreadyCamelCase")]
    [InlineData("User_Name", "user_Name")]
    [InlineData("user.Profile.HOME_ADDRESS", "user.profile.hOME_ADDRESS")]
    public void CamelizePropertyPath_ShouldCorrectlyConvertToCamelCase(string input, string expected)
    {
        // when
        var result = input.CamelizePropertyPath();

        // then
        result.Should().Be(expected);
    }

    [Fact]
    public void should_return_null_when_camelize_property_path_with_null_input()
    {
        // given
        const string? input = null;

        // when
        var result = input.CamelizePropertyPath();

        // then
        result.Should().BeNull();
    }

    // Regression: empty path segments (consecutive, leading, or trailing dots) previously threw
    // IndexOutOfRangeException because the first char of an empty segment was indexed.
    [Theory]
    [InlineData("A..B", "a..b")]
    [InlineData("Trailing.", "trailing.")]
    [InlineData(".Leading", "leading")]
    public void should_not_throw_when_camelize_property_path_with_empty_segments(string input, string expected)
    {
        // when
        var result = input.CamelizePropertyPath();

        // then
        result.Should().Be(expected);
    }

    // Regression: slicing at a UTF-16 index must not split a surrogate pair into a lone surrogate.
    [Fact]
    public void should_not_split_surrogate_pairs_when_truncate_end()
    {
        const string input = "ab" + _Emoji + "cd"; // 6 UTF-16 chars: a, b, high, low, c, d

        // Cut at index 3 would orphan the high surrogate, so it backs off to 2.
        input.TruncateEnd(3).Should().Be("ab");

        // Cut at index 4 falls on a code-point boundary, keeping the whole emoji.
        input.TruncateEnd(4).Should().Be("ab" + _Emoji);
    }

    [Fact]
    public void should_not_split_surrogate_pairs_when_truncate_end_with_suffix()
    {
        const string input = "ab" + _Emoji + "cd"; // 6 UTF-16 chars

        // cut = maxLength(4) - suffix(1) = 3 would orphan the high surrogate, so it backs off to 2.
        input.TruncateEnd(4, "_").Should().Be("ab_");
    }

    [Fact]
    public void should_not_split_surrogate_pairs_when_truncate_start()
    {
        const string input = "ab" + _Emoji + "cd"; // 6 UTF-16 chars

        // Keeping the last 3 chars would start on the orphaned low surrogate, so the start advances by one.
        input.TruncateStart(3).Should().Be("cd");

        // Keeping the last 4 chars starts on a code-point boundary, keeping the whole emoji.
        input.TruncateStart(4).Should().Be(_Emoji + "cd");
    }

    [Theory]
    [InlineData("192.168.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("255.255.255.255")]
    public void should_accept_valid_addresses_when_is_ip4(string value)
    {
        value.IsIp4().Should().BeTrue();
    }

    [Theory]
    // Regression: NumberStyles.None must reject a leading sign and surrounding whitespace per octet.
    [InlineData("+1.2.3.4")]
    [InlineData("1.2.3.+4")]
    [InlineData(" 1.2.3.4")]
    [InlineData("1.2.3.4 ")]
    [InlineData("1. 2.3.4")]
    // Structurally invalid.
    [InlineData("256.1.1.1")]
    [InlineData("1.2.3")]
    [InlineData("1.2.3.4.5")]
    [InlineData("abc")]
    [InlineData("")]
    public void IsIp4_should_reject_invalid_addresses(string value)
    {
        value.IsIp4().Should().BeFalse();
    }

    [Fact]
    public void remove_character_tests()
    {
        "a-b-c".RemoveCharacter('-').Should().Be("abc");
        // Absent character returns the input unchanged.
        "abc".RemoveCharacter('-').Should().Be("abc");
        ((string?)null).RemoveCharacter('-').Should().BeNull();
    }

    [Fact]
    public void remove_characters_tests()
    {
        "a-b_c".RemoveCharacters('-', '_').Should().Be("abc");
        // Absent characters return the input unchanged.
        "abc".RemoveCharacters('-', '_').Should().Be("abc");
        // Empty separator set strips whitespace (mirrors string.Split).
        "a b\tc".RemoveCharacters().Should().Be("abc");
        ((string?)null).RemoveCharacters('-').Should().BeNull();
    }

    [Fact]
    public void should_return_input_unchanged_when_normalize_line_endings_no_line_breaks()
    {
        const string input = "no line breaks here";

        input.NormalizeLineEndings().Should().Be(input);
    }

    [Fact]
    public void should_normalize_all_break_styles_when_normalize_line_endings()
    {
        const string input = "a\r\nb\rc\nd";

        var expected = string.Join(Environment.NewLine, "a", "b", "c", "d");

        input.NormalizeLineEndings().Should().Be(expected);
    }
}
