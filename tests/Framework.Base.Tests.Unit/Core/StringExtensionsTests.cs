// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Core;

namespace Tests.Core;

public sealed class StringExtensionsTests(ITestOutputHelper output) : IDisposable
{
    private readonly IDisposable _cultureScope = CultureHelper.Use("en-US");

    public void Dispose()
    {
        _cultureScope.Dispose();
    }

    [Fact]
    public static void IsNullOrEmpty_should_allows_null()
    {
        var result = ((string?)null).IsNullOrEmpty();
        result.Should().BeTrue();
    }

    [Fact]
    public static void IsNullOrWhiteSpace_should_allows_null()
    {
        var result = ((string?)null).IsNullOrWhiteSpace();
        result.Should().BeTrue();
    }

    [Fact]
    public void NullIfEmpty_tests()
    {
        ((string?)null).NullIfEmpty().Should().BeNull();
        "".NullIfEmpty().Should().BeNull();
        "hi".NullIfEmpty().Should().Be("hi");
    }

    [Fact]
    public void NullIfWhiteSpace_tests()
    {
        ((string?)null).NullIfWhiteSpace().Should().BeNull();
        "".NullIfWhiteSpace().Should().BeNull();
        "\r\n".NullIfWhiteSpace().Should().BeNull();
        "hi".NullIfWhiteSpace().Should().Be("hi");
    }

    [Fact]
    public void NormalizeLineEndings_tests()
    {
        const string str = "This\r\n is a\r test \n string";

        var normalized = str.NormalizeLineEndings();
        var lines = normalized.SplitToLines();

        lines.Should().HaveCount(4);
    }

    [Fact]
    public void EnsureEndsWith_tests()
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
    public void EnsureEndsWith_culture_specific_tests()
    {
        using (CultureHelper.Use("tr-TR"))
        {
            "Kırmızı".EnsureEndsWith('I', StringComparison.CurrentCultureIgnoreCase).Should().Be("Kırmızı");
        }
    }

    [Fact]
    public void EnsureStartsWith_tests()
    {
        // Expected use-cases
        "Test".EnsureStartsWith('~').Should().Be("~Test");
        "~Test".EnsureStartsWith('~').Should().Be("~Test");

        // Case differences
        "Egypt".EnsureStartsWith('t').Should().Be("tEgypt");
    }

    [Fact]
    public void RemovePostfix_tests()
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
    }

    [Fact]
    public void RemovePrefix_tests()
    {
        "Home.Index".RemovePrefix(StringComparison.Ordinal, "NotMatchedPostfix").Should().Be("Home.Index");
        "Home.About".RemovePrefix(StringComparison.Ordinal, "Home.").Should().Be("About");

        //Ignore case
        "Https://google.com".RemovePrefix(StringComparison.OrdinalIgnoreCase, "https://").Should().Be("google.com");
    }

    [Fact]
    public void TruncateEnd_tests()
    {
        const string str = "This is a test string";
        const string? nullValue = null;

        str.TruncateEnd(7).Should().Be("This is");
        str.TruncateEnd(0).Should().Be("");
        str.TruncateEnd(100).Should().Be(str);

        nullValue.TruncateEnd(5).Should().Be(null);
    }

    [Fact]
    public void TruncateEnd_with_postfix_overload_tests()
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
    public void OneSpace_tests()
    {
        "   ".OneSpace().Should().Be(" ");
        "\n\n\n".OneSpace().Should().Be(" ");
        "This\r\n is a\r test \n string".OneSpace().Should().Be("This is a test string");
    }

    [Fact]
    public void NthIndexOf_tests()
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
    public void GetBytes_tests(string str)
    {
        var bytes = str.GetBytes();
        bytes.Should().NotBeNull();
        bytes.Length.Should().BeGreaterThanOrEqualTo(str.Length);
        Encoding.UTF8.GetString(bytes).Should().Be(str);
    }

    [Theory]
    [InlineData("")]
    [InlineData("MyString")]
    public void GetBytes_with_encoding_tests(string str)
    {
        var bytes = str.GetBytes(Encoding.ASCII);
        bytes.Should().NotBeNull();
        bytes.Length.Should().BeGreaterThanOrEqualTo(str.Length);
        Encoding.ASCII.GetString(bytes).Should().Be(str);
    }

    [Fact]
    public void ToEnum_tests()
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
        // act
        var result = value.RemoveAccentCharacters();

        output.WriteLine($"result   =>{result}");
        output.WriteLine($"expected =>{expected}");

        // assert
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
        // Act
        var result = input.CamelizePropertyPath();

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void CamelizePropertyPath_WithNullInput_ShouldReturnNull()
    {
        // Arrange
        const string? input = null;

        // Act
        var result = input.CamelizePropertyPath();

        // Assert
        result.Should().BeNull();
    }
}
