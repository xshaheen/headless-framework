// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Serializer.Converters;

namespace Tests.Converters;

public sealed class StringToBooleanJsonConverterTests
{
    private readonly JsonSerializerOptions _jsonOptions = new() { Converters = { new StringToBooleanJsonConverter() } };

    [Fact]
    public void should_convert_valid_true_boolean_successfully_when_string_to_bool_converter()
    {
        // given
        const string json = "\"true\"";

        // when
        var result = JsonSerializer.Deserialize<bool>(json, _jsonOptions);

        // then
        result.Should().BeTrue();
    }

    [Fact]
    public void should_convert_valid_false_boolean_successfully_when_string_to_bool_converter()
    {
        // given
        const string json = "\"false\"";

        // when
        var result = JsonSerializer.Deserialize<bool>(json, _jsonOptions);

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public void should_throw_when_string_to_bool_converter_converting_invalid_bool_json()
    {
        // given
        const string json = "\"not a bool\"";

        // when
        Action act = () => JsonSerializer.Deserialize<bool>(json, _jsonOptions);

        // then
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void should_throw_exception_for_null_json_string_when_string_to_bool_converter()
    {
        // given
        const string? json = "null";

        // when
        Action act = () => JsonSerializer.Deserialize<bool>(json, _jsonOptions);

        // then
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void should_throw_for_empty_json_string_when_string_to_bool_converter()
    {
        // given
        const string json = "\"\"";

        // when
        Action act = () => JsonSerializer.Deserialize<bool>(json, _jsonOptions);

        // then
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void should_write_valid_true_boolean_to_json_when_string_to_bool_converter()
    {
        // given
        const bool value = true;

        // when
        var json = JsonSerializer.Serialize(value, _jsonOptions);

        // then
        json.Should().Be("true");
    }

    [Fact]
    public void should_write_valid_false_boolean_to_json_when_string_to_bool_converter()
    {
        // given
        const bool value = false;

        // when
        var json = JsonSerializer.Serialize(value, _jsonOptions);

        // then
        json.Should().Be("false");
    }
}
