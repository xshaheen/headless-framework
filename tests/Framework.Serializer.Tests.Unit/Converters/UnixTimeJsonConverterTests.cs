// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Serializer.Converters;

namespace Tests.Converters;

public sealed class UnixTimeJsonConverterTests
{
    private readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        Converters = { new UnixTimeJsonConverter() },
    };

    [Fact]
    public void unix_time_converter_should_successfully_read_valid_unix_timestamp_json()
    {
        // given
        const long unixTimestamp = 1631564345;
        var json = unixTimestamp.ToString(CultureInfo.InvariantCulture);

        // when
        var result = JsonSerializer.Deserialize<DateTimeOffset>(json, _jsonSerializerOptions);

        // then
        result.Should().Be(DateTimeOffset.FromUnixTimeSeconds(unixTimestamp));
    }

    [Fact]
    public void unix_time_converter_should_throw_exception_for_invalid_timestamp_json()
    {
        // given
#pragma warning disable JSON001 // Invalid JSON pattern
        const string invalidJson = "invalid_timestamp";
#pragma warning restore JSON001 // Invalid JSON pattern

        // when
        Action act = () => JsonSerializer.Deserialize<DateTimeOffset>(invalidJson, _jsonSerializerOptions);

        // then
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void unix_time_converter_should_convert_date_time_offset_to_unix_timestamp()
    {
        // given
        var dateTimeOffset = new DateTimeOffset(2021, 9, 14, 9, 25, 45, DateTimeOffset.Now.Offset);

        // when
        var json = JsonSerializer.Serialize(dateTimeOffset, _jsonSerializerOptions);

        // then
        json.Should().Be(dateTimeOffset.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void unix_time_converter_should_write_min_value_of_date_time_offset()
    {
        // given
        var dateTimeOffset = DateTimeOffset.MinValue;

        // when
        var json = JsonSerializer.Serialize(dateTimeOffset, _jsonSerializerOptions);

        // then
        json.Should().Be(dateTimeOffset.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void unix_time_converter_should_write_max_value_of_date_time_offset()
    {
        // given
        var dateTimeOffset = DateTimeOffset.MaxValue;

        // when
        var json = JsonSerializer.Serialize(dateTimeOffset, _jsonSerializerOptions);

        // then
        json.Should().Be(dateTimeOffset.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void unix_time_converter_should_handle_negative_timestamp()
    {
        // given
        const long negativeUnixTimestamp = -1;
        var json = negativeUnixTimestamp.ToString(CultureInfo.InvariantCulture);

        // when
        var result = JsonSerializer.Deserialize<DateTimeOffset>(json, _jsonSerializerOptions);

        // then
        result.Should().Be(DateTimeOffset.FromUnixTimeSeconds(negativeUnixTimestamp));
    }

    [Fact]
    public void unix_time_converter_should_throw_json_exception_on_non_number_values()
    {
        // given
        const string nonNumberJson = "\"Not a number\"";

        // when
        Action act = () => JsonSerializer.Deserialize<DateTimeOffset>(nonNumberJson, _jsonSerializerOptions);

        // then
        act.Should().Throw<FormatException>();
    }
}
