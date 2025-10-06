// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;

namespace Tests.Extensions;

public sealed class ToObjectExtensionsTests
{
    [Theory]
    [InlineData("-1", -1)]
    [InlineData("0", 0)]
    [InlineData("42", 42)]
    public void To_ValidIntegerString_ReturnsInteger(string input, int expected)
    {
        input.To<int>().Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("test")]
    [InlineData("2.0")]
    public void To_InvalidIntegerString_ThrowsFormatException(string input)
    {
        var action = () => input.To<int>();
        action.Should().ThrowExactly<FormatException>();
    }

    [Theory]
    [InlineData("1", 1L)]
    [InlineData("28173829281734", 28173829281734L)]
    public void To_ValidLongString_ReturnsLong(string input, long expected)
    {
        input.To<long>().Should().Be(expected);
    }

    [Theory]
    [InlineData("2.0", 2.0)]
    [InlineData("0.2", 0.2)]
    public void To_ValidDoubleString_ReturnsDouble(string input, double expected)
    {
        input.To<double>().Should().Be(expected);
    }

    [Theory]
    [InlineData("false", false)]
    [InlineData("True", true)]
    [InlineData("False", false)]
    [InlineData("TrUE", true)]
    public void To_ValidBooleanString_ReturnsBoolean(string input, bool expected)
    {
        input.To<bool>().Should().Be(expected);
    }

    [Theory]
    [InlineData("test")]
    [InlineData("T")]
    [InlineData("F")]
    public void To_InvalidBooleanString_ThrowsFormatException(string input)
    {
        var action = () => input.To<bool>();
        action.Should().ThrowExactly<FormatException>();
    }

    [Fact]
    public void To_ValidGuidString_ReturnsGuid()
    {
        const string guidString = "2260AFEC-BBFD-42D4-A91A-DCB11E09B17F";
        var expected = new Guid(0x2260AFEC, 0xBBFD, 0x42D4, 0xA9, 0x1A, 0xDC, 0xB1, 0x1E, 0x9, 0xB1, 0x7F) /* 2260AFEC-BBFD-42D4-A91A-DCB11E09B17F */;

        guidString.To<Guid>().Should().Be(expected);
    }

    [Fact]
    public void To_JsonElement_DeserializesCorrectly()
    {
        var json = JsonSerializer.Serialize(new { Name = "Test", Value = 42 });
        var element = JsonSerializer.Deserialize<JsonElement>(json);

        var result = element.To<TestClass>();

        result.Should().NotBeNull();
        result.Name.Should().Be("Test");
        result.Value.Should().Be(42);
    }

    [Fact]
    public void To_JsonNode_DeserializesCorrectly()
    {
        var jsonNode = JsonNode.Parse("""{"Name":"Test", "Value":42}""");

        var result = jsonNode.To<TestClass>();

        result.Should().NotBeNull();
        result.Name.Should().Be("Test");
        result.Value.Should().Be(42);
    }

    [Fact]
    public void To_JsonDocument_DeserializesCorrectly()
    {
        using var document = JsonDocument.Parse("""{"Name":"Test", "Value":42}""");

        var result = document.To<TestClass>();

        result.Should().NotBeNull();
        result.Name.Should().Be("Test");
        result.Value.Should().Be(42);
    }

    [Fact]
    public void To_NullObject_ReturnsDefault()
    {
        object? nullObj = null;

        var result = nullObj.To<TestClass>();

        result.Should().BeNull();
    }

    [Fact]
    public void To_Enum_ParsesCorrectly()
    {
        const string input = "Value2";

        var result = input.To<TestEnum>();

        result.Should().Be(TestEnum.Value2);
    }

    [Theory]
    [InlineData("InvalidValue")]
    public void To_InvalidEnum_ThrowsArgumentException(string input)
    {
        var action = () => input.To<TestEnum>();

        action.Should().ThrowExactly<ArgumentException>();
    }

    private sealed class TestClass
    {
        public string Name { get; set; } = string.Empty;

        public int Value { get; set; }
    }

    private enum TestEnum
    {
        Value1,
        Value2,
        Value3,
    }
}
