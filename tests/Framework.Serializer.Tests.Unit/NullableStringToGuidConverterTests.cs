// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using Framework.Serializer.Json.Converters;

namespace Tests;

public class NullableStringToGuidConverterTests
{
    private readonly JsonSerializerOptions _options = new()
    {
        Converters = { new NullableStringToGuidConverter() },
    };

    [Fact]
    public void guid_convertor_should_deserialize_valid_guild_normally()
    {
        // given
        const string json = "\"123e4567-e89b-12d3-a456-426614174000\"";

        // when
        var result = JsonSerializer.Deserialize<Guid?>(json, _options);

        // then
        result.Should().Be(Guid.Parse("123e4567-e89b-12d3-a456-426614174000"));
    }

    [Fact]
    public void guid_convertor_should_throw_exception_when_serializing_invalid_guid()
    {
        // given
        const string json = "\"12qqqqq67-e89b-12d3-a456-426614174000\"";

        // when
        Action act = () => JsonSerializer.Deserialize<Guid?>(json, _options);

        // then
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void guid_convertor_returns_null_when_the_json_is_null()
    {
        // given
        const string json = "null";

        // when
        var result = JsonSerializer.Deserialize<Guid?>(json, _options);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public void should_return_guid_for_empty_string()
    {
        // given
        const string json = "\"\"";

        // when
        Action act = () => JsonSerializer.Deserialize<Guid?>(json, _options);

        // then
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void should_throw_json_exception_for_invalid_guid_format()
    {
        // given
        const string json = "\"invalid-guid-format\"";

        // when
        Action act = () => JsonSerializer.Deserialize<Guid?>(json, _options);

        // then
        act.Should().Throw<JsonException>();
    }
}
