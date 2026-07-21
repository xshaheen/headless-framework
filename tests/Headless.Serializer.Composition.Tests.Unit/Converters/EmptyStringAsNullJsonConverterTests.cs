// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Serializer.Converters;

namespace Tests.Converters;

public sealed class EmptyStringAsNullJsonConverterTests
{
    private readonly JsonSerializerOptions _options = new()
    {
        Converters = { new EmptyStringAsNullJsonConverter<string>() },
    };

    [Fact]
    public void should_convert_empty_string_to_null()
    {
        // given
        const string json = """{"value":""}""";

        // when
        var result = JsonSerializer.Deserialize<TestModel>(json, _options);

        // then
        result.Should().NotBeNull();
        result!.Value.Should().BeNull();
    }

    [Fact]
    public void should_preserve_non_empty_string()
    {
        // given
        const string json = """{"value":"hello"}""";

        // when
        var result = JsonSerializer.Deserialize<TestModel>(json, _options);

        // then
        result.Should().NotBeNull();
        result!.Value.Should().Be("hello");
    }

    [Fact]
    public void should_preserve_null_value()
    {
        // given
        const string json = """{"value":null}""";

        // when
        var result = JsonSerializer.Deserialize<TestModel>(json, _options);

        // then
        result.Should().NotBeNull();
        result!.Value.Should().BeNull();
    }

    [Fact]
    public void should_preserve_whitespace_string()
    {
        // given - converter only converts empty string, not whitespace
        const string json = """{"value":" "}""";

        // when
        var result = JsonSerializer.Deserialize<TestModel>(json, _options);

        // then
        result.Should().NotBeNull();
        result!.Value.Should().Be(" ");
    }

    [Fact]
    public void should_write_null_as_null()
    {
        // given
        var model = new TestModel { Value = null };

        // when
        var json = JsonSerializer.Serialize(model, _options);

        // then
        json.Should().Be("""{"value":null}""");
    }

    [Fact]
    public void should_write_empty_string_as_null()
    {
        // given
        var model = new TestModel { Value = "" };

        // when
        var json = JsonSerializer.Serialize(model, _options);

        // then
        json.Should().Be("""{"value":null}""");
    }

    [Fact]
    public void should_write_non_empty_string_as_string()
    {
        // given
        var model = new TestModel { Value = "hello" };

        // when
        var json = JsonSerializer.Serialize(model, _options);

        // then
        json.Should().Be("""{"value":"hello"}""");
    }

    private sealed class TestModel
    {
        [JsonPropertyName("value")]
        [JsonConverter(typeof(EmptyStringAsNullJsonConverter<string>))]
        public string? Value { get; set; }
    }
}
