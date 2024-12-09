// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Text.Json;
using Framework.Serializer.Json.Converters;

namespace Tests.Converters;

public class IpAddressJsonConverterTests
{
    private readonly JsonSerializerOptions _options = new() { Converters = { new IpAddressJsonConverter() } };

    [Fact]
    public void ip_json_converter_should_return_valid_ip_object_when_reading_valid_ip_json()
    {
        // given
        const string json = "\"192.168.1.1\"";

        // when
        var result = JsonSerializer.Deserialize<IPAddress?>(json, _options);

        // then
        result.Should().Be(IPAddress.Parse("192.168.1.1"));
    }

    [Fact]
    public void ip_json_converter_should_return_null_ip_object_when_reading_null_json_string()
    {
        // given
        const string json = "null";

        // when
        var result = JsonSerializer.Deserialize<IPAddress?>(json, _options);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public void ip_json_converter_should_write_valid_ip_string_json_when_writing_valid_ip()
    {
        // given
        var ip = IPAddress.Parse("192.168.1.1");

        // when
        var result = JsonSerializer.Serialize(ip, _options);

        // then
        result.Should().Be("\"192.168.1.1\"");
    }

    [Fact]
    public void ip_json_converter_should_write_null_json_string_when_writing_null_ip_object()
    {
        // given
        IPAddress? ip = null;

        // when
        var result = JsonSerializer.Serialize(ip, _options);

        // then
        result.Should().Be("null");
    }

    [Fact]
    public void ip_json_converter_should_throw_when_reading_invalid_ip_json_string()
    {
        // given
        const string json = "\"999.999.999.999\"";

        // when
        Action act = () => JsonSerializer.Deserialize<IPAddress?>(json, _options);

        // then
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void ip_json_converter_should_throw_when_reading_empty_json_string()
    {
        // given
        const string json = "\"\"";

        // when
        Action act = () => JsonSerializer.Deserialize<IPAddress?>(json, _options);

        // then
        act.Should().Throw<FormatException>();
    }
}
