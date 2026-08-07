// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs;
using Headless.Jobs.Infrastructure;
using Headless.Testing.Tests;

namespace Tests.Dashboard;

public sealed class StringToByteArrayConverterTests : TestBase
{
    private static readonly JobsRequestSerializationOptions _SerializationOptions =
        JobsRequestSerializationOptions.Default;
    private static readonly JsonSerializerOptions _JsonOptions = new()
    {
        Converters = { new StringToByteArrayConverter(_SerializationOptions) },
    };

    [Theory]
    [InlineData("null")]
    [InlineData("\"\"")]
    public void should_deserialize_to_null_when_token_is_null_or_empty(string json)
    {
        JsonSerializer.Deserialize<byte[]?>(json, _JsonOptions).Should().BeNull();
    }

    [Fact]
    public void should_reject_input_when_json_token_is_an_object()
    {
        var act = () => JsonSerializer.Deserialize<byte[]?>("{}", _JsonOptions);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void should_round_trip_the_request_when_json_token_is_a_string()
    {
        const string requestJson = "{\"name\":\"job\",\"count\":2}";
        var encodedJson = JsonSerializer.Serialize(requestJson);

        var bytes = JsonSerializer.Deserialize<byte[]>(encodedJson, _JsonOptions);
        var serialized = JsonSerializer.Serialize(bytes, _JsonOptions);

        bytes.Should().NotBeNull();
        JobsHelper.ReadJobRequestAsString(bytes!, _SerializationOptions).Should().Be(requestJson);
        JsonSerializer.Deserialize<string>(serialized).Should().Be(requestJson);
    }
}
