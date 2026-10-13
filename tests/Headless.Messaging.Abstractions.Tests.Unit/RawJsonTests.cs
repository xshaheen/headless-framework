// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Testing.Tests;

namespace Tests;

public sealed class RawJsonTests : TestBase
{
    [Theory]
    [InlineData("""{"run_id":"r1","items":[1,2]}""")]
    [InlineData("""  [ 1 , "two" ]  """)]
    [InlineData("\"text\"")]
    [InlineData("42")]
    public void should_keep_the_exact_bytes_of_one_json_value(string json)
    {
        // given
        var bytes = Encoding.UTF8.GetBytes(json);

        // when
        var raw = new RawJson(bytes);

        // then
        raw.Utf8Json.ToArray().Should().Equal(bytes);
        raw.ToString().Should().Be(json);
    }

    [Fact]
    public void should_copy_the_input_so_a_later_change_to_the_caller_buffer_is_not_seen()
    {
        // given
        var bytes = """{"a":1}"""u8.ToArray();
        var raw = new RawJson(bytes);

        // when
        bytes[5] = (byte)'9';

        // then
        raw.ToString().Should().Be("""{"a":1}""");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{")]
    [InlineData("not json")]
    [InlineData("""{"a":1}{"b":2}""")]
    [InlineData("1 2")]
    public void should_reject_input_that_is_not_one_complete_json_value(string json)
    {
        // when
        var act = () => new RawJson(Encoding.UTF8.GetBytes(json));

        // then
        act.Should().Throw<ArgumentException>().WithParameterName("utf8Json").WithInnerException<JsonException>();
    }

    [Fact]
    public void should_bypass_the_naming_policy_and_unknown_member_rules_of_the_serializer_options()
    {
        // given - options that would reject or rename this body if they bound it
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        };
        const string json = """{ "run_id" : "r1", "note" : "café ☃" }""";

        // when
        var read = JsonSerializer.Deserialize<RawJson>(json, options);
        var written = JsonSerializer.Serialize(read, options);

        // then
        read.Should().NotBeNull();
        read.ToString().Should().Be(json);
        written.Should().Be(json);
    }

    [Fact]
    public void should_read_a_json_null_as_no_message()
    {
        // when
        var read = JsonSerializer.Deserialize<RawJson>("null");

        // then
        read.Should().BeNull();
    }

    [Fact]
    public void should_keep_the_exact_bytes_when_embedded_in_an_envelope_and_read_back_from_an_element()
    {
        // given - the storage path writes the body inside an envelope, then reads it back as a JsonElement
        const string json = """{"z":1,  "a":"A"}""";
        var raw = new RawJson(Encoding.UTF8.GetBytes(json));

        // when
        var envelope = JsonSerializer.Serialize(new Dictionary<string, object> { ["value"] = raw });
        var element = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(envelope)!["value"];
        var readBack = element.Deserialize<RawJson>();

        // then
        envelope.Should().Be($$"""{"value":{{json}}}""");
        readBack!.ToString().Should().Be(json);
    }
}
