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
    public void object_to_types_converter_should_convert_valid_true_bool_successfully()
    {
        // given
        const string json = "true";

        // when
        var result = JsonSerializer.Deserialize<object>(json, _jsonOptions);

        // then
        result.Should().Be(true);
    }

    [Fact]
    public void object_to_types_converter_should_convert_valid_false_bool_successfully()
    {
        // given
        const string json = "false";

        // when
        var result = JsonSerializer.Deserialize<object>(json, _jsonOptions);

        // then
        result.Should().Be(false);
    }

    [Fact]
    public void object_to_types_converter_should_convert_valid_numerical_value_successfully()
    {
        // given
        const string json = "42";

        // when
        var result = JsonSerializer.Deserialize<object>(json, _jsonOptions);

        // then
        result.Should().Be(42L);
    }

    [Fact]
    public void object_to_types_converter_should_convert_valid_double_value_successfully()
    {
        // given
        const string json = "42.5";

        // when
        var result = JsonSerializer.Deserialize<object>(json, _jsonOptions);

        // then
        result.Should().Be(42.5);
    }

    [Fact]
    public void object_to_types_converter_should_convert_valid_datetime_successfully()
    {
        // given
        const string json = "\"2024-01-01T12:00:00\"";

        // when
        var result = JsonSerializer.Deserialize<object>(json, _jsonOptions);

        // then
        result.Should().BeOfType<DateTimeOffset>();
        var dateTimeOffset = (DateTimeOffset)result;
        dateTimeOffset.Year.Should().Be(2024);
        dateTimeOffset.Month.Should().Be(1);
        dateTimeOffset.Day.Should().Be(1);
        dateTimeOffset.Hour.Should().Be(12);
        dateTimeOffset.Minute.Should().Be(0);
        dateTimeOffset.Second.Should().Be(0);
    }

    [Fact]
    public void object_to_types_converter_should_convert_valid_string_value_successfully()
    {
        // given
        const string json = "\"hello world\"";

        // when
        var result = JsonSerializer.Deserialize<string>(json, _jsonOptions);

        // then
        result.Should().Be("hello world");
    }

    [Fact]
    public void object_to_types_converter_should_convert_valid_object_type_successfully()
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
    public void object_to_types_converter_should_throw_when_trying_to_convert_invalid_json()
    {
        // given
#pragma warning disable JSON001 // Invalid JSON pattern
        const string json = "{ this is invalid JSON ";
#pragma warning restore JSON001 // Invalid JSON pattern

        // when
        Action act = () => JsonSerializer.Deserialize<object>(json, _jsonOptions);

        // then
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void object_to_types_converter_should_write_null_when_reading_null_object()
    {
        // given
        object? value = null;

        // when
        var json = JsonSerializer.Serialize(value, _jsonOptions);

        // then
        json.Should().Be("null");
    }

    [Fact]
    public void object_to_types_converter_should_write_valid_object_types_successfully()
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
