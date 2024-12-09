// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Serializer.Json.Converters;

namespace Tests.Converters;

public class ObjectToInferredTypesConverterTests
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        Converters = { new ObjectToInferredTypesConverter() },
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
        result.Should().Be(new DateTimeOffset(2024, 1, 1, 12, 0, 0, DateTimeOffset.Now.Offset));
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

    // TODO: warning this is throwing stack overflow exception, ask Shaheen about it.
    // [Fact]
    // public void object_to_types_converter_should_convert_valid_object_type_successfully()
    // {
    //     // given
    //     const string json = "{\"property\": \"value\"}";
    //
    //     // when
    //     var result = JsonSerializer.Deserialize<object?>(json, _jsonOptions);
    //
    //     // then
    //     result.Should().NotBeNull();
    //     result.Should().BeOfType<object>();
    // }

    [Fact]
    public void object_to_types_converter_should_throw_when_trying_to_convert_invalid_json()
    {
        // given
        const string json = "{ this is invalid JSON ";

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
    public void object_to_types_converter_should_write_valid_object_types_sucessfully()
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
