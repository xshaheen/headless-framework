// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Serializer.Converters;

namespace Tests.Converters;

public class NullableStringToGuidJsonConverterTests
{
    private readonly JsonSerializerOptions _options = new()
    {
        Converters = { new NullableStringToGuidJsonConverter() },
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
    public void guid_convertor_should_returns_null_when_the_json_is_null()
    {
        // given
        const string json = "null";

        // when
        var result = JsonSerializer.Deserialize<Guid?>(json, _options);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public void guid_convertor_should_throw_while_reading_empty_json()
    {
        // given
        const string json = "\"\"";

        // when
        Action act = () => JsonSerializer.Deserialize<Guid?>(json, _options);

        // then
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void guid_convertor_should_throw_while_reading_invalid_guid_format_in_json()
    {
        // given
        const string json = "\"invalid-guid-format\"";

        // when
        Action act = () => JsonSerializer.Deserialize<Guid?>(json, _options);

        // then
        act.Should().Throw<JsonException>();
    }
}
