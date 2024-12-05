// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using Framework.Serializer.Json.Converters;

namespace Tests.Converters;

public class ObjectToInferredTypesConverterTests
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        Converters = { new ObjectToInferredTypesConverter() },
    };

    [Fact]
    public void read_should_return_true_when_json_is_true()
    {
        // given
        const string json = "true";

        // when
        var result = JsonSerializer.Deserialize<object>(json, _jsonOptions);

        // then
        result.Should().Be(true);
    }

    [Fact]
    public void read_should_return_false_when_json_is_false()
    {
        // given
        const string json = "false";

        // when
        var result = JsonSerializer.Deserialize<object>(json, _jsonOptions);

        // then
        result.Should().Be(false);
    }

    [Fact]
    public void read_should_return_int_when_json_is_integer()
    {
        // given
        const string json = "42";

        // when
        var result = JsonSerializer.Deserialize<object>(json, _jsonOptions);

        // then
        result.Should().Be(42L);
    }

    [Fact]
    public void read_should_return_double_when_json_is_float()
    {
        // given
        const string json = "42.5";

        // when
        var result = JsonSerializer.Deserialize<object>(json, _jsonOptions);

        // then
        result.Should().Be(42.5);
    }

    [Fact]
    public void converter_should_return_datetime_when_json_is_date_time()
    {
        // given
        const string json = "\"2024-01-01T12:00:00\"";

        // when
        var result = JsonSerializer.Deserialize<object>(json, _jsonOptions);

        // then
        result.Should().Be(new DateTimeOffset(2024, 1, 1, 12, 0, 0, DateTimeOffset.Now.Offset));
    }

    [Fact]
    public void read_should_return_string_when_json_is_string()
    {
        // given
        const string json = "\"hello world\"";

        // when
        var result = JsonSerializer.Deserialize<object>(json, _jsonOptions);

        // then
        result.Should().Be("hello world");
    }

    [Fact]
    public void read_should_return_object_when_json_is_invalid()
    {
        # warning this is throwing stack overflow exception, ask Shaheen about it.
        // given
        const string json = "{\"property\": \"value\"}";

        // when
        var result = JsonSerializer.Deserialize<object?>(json, _jsonOptions);

        // then
        result.Should().NotBeNull();
        result.Should().BeOfType<object>();
    }

    [Fact]
    public void read_should_throw_exception_when_json_is_unexpected()
    {
        // given
        const string json = "{ this is invalid JSON }";

        // when
        Action act = () => JsonSerializer.Deserialize<object>(json, _jsonOptions);

        // then
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void write_should_handle_null_values_correctly()
    {
        // given
        object? value = null;

        // when
        var json = JsonSerializer.Serialize(value, _jsonOptions);

        // then
        json.Should().Be("null");
    }

    [Fact]
    public void write_should_serialize_object_correctly()
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
