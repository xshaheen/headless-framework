// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Text.Json;
using Framework.Serializer.Json.Converters;

namespace Tests;

public class IpAddressJsonConverterTests
{
    private readonly JsonSerializerOptions _options = new()
    {
        Converters = { new IpAddressJsonConverter() },
    };

    [Fact]
    public void given_valid_ip_string_when_read_then_should_return_ip_address()
    {
        const string json = "\"192.168.1.1\"";

        var result = JsonSerializer.Deserialize<IPAddress?>(json, _options);

        result.Should().Be(IPAddress.Parse("192.168.1.1"));
    }

    [Fact]
    public void given_null_ip_string_when_read_then_should_return_null()
    {
        const string json = "null";

        var result = JsonSerializer.Deserialize<IPAddress?>(json, _options);

        result.Should().BeNull();
    }

    [Fact]
    public void given_valid_ip_address_when_write_then_should_return_ip_string()
    {
        var ip = IPAddress.Parse("192.168.1.1");

        var result = JsonSerializer.Serialize(ip, _options);

        result.Should().Be("\"192.168.1.1\"");
    }

    [Fact]
    public void given_null_ip_address_when_write_then_should_return_null()
    {
        IPAddress? ip = null;

        var result = JsonSerializer.Serialize(ip, _options);

        result.Should().Be("null");
    }

    [Fact]
    public void given_invalid_ip_string_when_read_then_should_throw_format_exception()
    {
        const string json = "\"999.999.999.999\"";

        Action act = () => JsonSerializer.Deserialize<IPAddress?>(json, _options);

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void given_empty_string_when_read_then_should_return_null()
    {
        const string json = "\"\"";

        var result = JsonSerializer.Deserialize<IPAddress?>(json, _options);

        result.Should().BeNull();
    }
}
