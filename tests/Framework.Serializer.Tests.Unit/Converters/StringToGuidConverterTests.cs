// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Serializer.Json.Converters;

namespace Tests.Converters;

public class StringToGuidConverterTests
{
    private readonly JsonSerializerOptions _jsonOptions = new() { Converters = { new StringToGuidConverter() } };

    [Theory]
    [InlineData("d85b1407-351d-4694-9392-03acc5870eb1", "D")]
    [InlineData("d85b1407351d4694939203acc5870eb1", "N")]
    [InlineData("{d85b1407-351d-4694-9392-03acc5870eb1}", "B")]
    [InlineData("(d85b1407-351d-4694-9392-03acc5870eb1)", "P")]
    [InlineData("{0xD85B1407,0x351D,0x4694,{0x93,0x92,0x03,0xAC,0xC5,0x87,0x0E,0xB1}}", "X")]
    public void string_to_guid_converter_should_return_valid_guid_given_valid_guid_in_different_formats(
        string guidString,
        string format
    )
    {
        // given
        var json = $"\"{guidString}\"";

        // when
        var result = JsonSerializer.Deserialize<Guid>(json, _jsonOptions);

        // then
        result.Should().Be(Guid.ParseExact(guidString, format));
    }

    // Serialization Tests

    [Theory]
    [InlineData("d85b1407-351d-4694-9392-03acc5870eb1", "D")]
    [InlineData("d85b1407351d4694939203acc5870eb1", "N")]
    [InlineData("{d85b1407-351d-4694-9392-03acc5870eb1}", "B")]
    [InlineData("(d85b1407-351d-4694-9392-03acc5870eb1)", "P")]
    [InlineData("{0xD85B1407,0x351D,0x4694,{0x93,0x92,0x03,0xAC,0xC5,0x87,0x0E,0xB1}}", "X")]
    public void string_to_guid_converter_should_serialize_the_guid_formats_successfully(
        string guidString,
        string format
    )
    {
        // given
        var guid = Guid.ParseExact(guidString, format);

        // when
        var json = JsonSerializer.Serialize(guid, _jsonOptions);

        // then
        json.Should().Be($"\"{guid:D}\"");
    }

    [Theory]
    [InlineData("invalid-guid-string")]
    [InlineData("")]
    [InlineData("123412")]
    [InlineData(null)]
    public void string_to_guid_converter_should_throw_when_given_invalid_guids(string? invalidGuid)
    {
        // given
        var json = invalidGuid == null ? "null" : $"\"{invalidGuid}\"";

        // when
        Action act = () => JsonSerializer.Deserialize<Guid>(json, _jsonOptions);

        // then
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void string_to_guid_converter_should_write_in_normal_guid_format()
    {
        // given
        var guid = Guid.NewGuid();
        var expectedJson = $"\"{guid}\"";

        // when
        var json = JsonSerializer.Serialize(guid, _jsonOptions);

        // then
        json.Should().Be(expectedJson);
    }

    [Fact]
    public void string_to_guid_converter_should_throw_exception_when_reading_null_json_string()
    {
        // given
        const string json = "null";

        // when
        Action act = () => JsonSerializer.Deserialize<Guid>(json, _jsonOptions);

        // then
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void string_to_guid_converter_should_write_empty_guild_normally()
    {
        // given
        var guid = Guid.Empty;

        // when
        var json = JsonSerializer.Serialize(guid, _jsonOptions);

        // then
        json.Should().Be("\"00000000-0000-0000-0000-000000000000\"");
    }
}
