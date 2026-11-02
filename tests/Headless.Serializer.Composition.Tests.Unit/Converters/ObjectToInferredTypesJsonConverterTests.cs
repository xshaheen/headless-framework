// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Serializer.Converters;

namespace Tests.Converters;

public sealed class ObjectToInferredTypesJsonConverterTests
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        Converters = { new ObjectToInferredTypesJsonConverter() },
    };

    [Fact]
    public void should_convert_valid_true_bool_successfully_when_object_to_types_converter()
    {
        // given
        const string json = "true";

        // when
        var result = JsonSerializer.Deserialize<object>(json, _jsonOptions);

        // then
        result.Should().Be(true);
    }

    [Fact]
    public void should_convert_valid_false_bool_successfully_when_object_to_types_converter()
    {
        // given
        const string json = "false";

        // when
        var result = JsonSerializer.Deserialize<object>(json, _jsonOptions);

        // then
        result.Should().Be(false);
    }

    [Fact]
    public void should_convert_valid_numerical_value_successfully_when_object_to_types_converter()
    {
        // given
        const string json = "42";

        // when
        var result = JsonSerializer.Deserialize<object>(json, _jsonOptions);

        // then
        result.Should().Be(42L);
    }

    [Fact]
    public void should_convert_non_integer_number_to_decimal_when_object_to_types_converter()
    {
        // given
        const string json = "42.5";

        // when
        var result = JsonSerializer.Deserialize<object>(json, _jsonOptions);

        // then
        result.Should().Be(42.5m);
    }

    [Fact]
    public void should_keep_every_digit_of_a_decimal_when_object_to_types_converter()
    {
        // given
        const string json = "12345678901234567.89";

        // when
        var result = JsonSerializer.Deserialize<object>(json, _jsonOptions);

        // then
        result.Should().Be(12345678901234567.89m);
    }

    [Theory]
    [InlineData("1.5e300", 1.5e300)]
    [InlineData("1.234e-27", 1.234e-27)]
    [InlineData("5e-324", 5e-324)]
    public void should_fall_back_to_double_when_decimal_cannot_hold_the_number(string json, double expected)
    {
        // when
        var result = JsonSerializer.Deserialize<object>(json, _jsonOptions);

        // then
        result.Should().BeOfType<double>().Which.Should().Be(expected);
    }

    [Fact]
    public void should_keep_date_like_string_as_string_when_object_to_types_converter()
    {
        // given - JSON has no date type, so a string that happens to look like a date stays the string it was
        const string json = "\"2024-01-01T12:00:00\"";

        // when
        var result = JsonSerializer.Deserialize<object>(json, _jsonOptions);

        // then
        result.Should().Be("2024-01-01T12:00:00");
    }

    [Fact]
    public void should_convert_valid_string_value_successfully_when_object_to_types_converter()
    {
        // given
        const string json = "\"hello world\"";

        // when
        var result = JsonSerializer.Deserialize<string>(json, _jsonOptions);

        // then
        result.Should().Be("hello world");
    }

    [Fact]
    public void should_convert_valid_object_type_successfully_when_object_to_types_converter()
    {
        // given
        const string json = "{\"property\": \"value\"}";

        // when
        var result = JsonSerializer.Deserialize<object?>(json, _jsonOptions);

        // then
        result.Should().NotBeNull();
        result.Should().BeOfType<JsonElement>();
    }

    [Fact]
    public void should_throw_when_object_to_types_converter_trying_to_convert_invalid_json()
    {
        // given
        const string json = "{ this is invalid JSON ";

        // when
        Action act = () => JsonSerializer.Deserialize<object>(json, _jsonOptions);

        // then
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void should_write_null_when_object_to_types_converter_reading_null_object()
    {
        // given
        object? value = null;

        // when
        var json = JsonSerializer.Serialize(value, _jsonOptions);

        // then
        json.Should().Be("null");
    }

    [Fact]
    public void should_write_valid_object_types_successfully_when_object_to_types_converter()
    {
        // given
        var value = new { Name = "Test", Age = 30 };

        // when
        var json = JsonSerializer.Serialize(value, _jsonOptions);

        // then
        json.Should().Contain("\"Name\":\"Test\"");
        json.Should().Contain("\"Age\":30");
    }
}
