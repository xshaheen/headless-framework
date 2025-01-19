// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Serializer;
using Framework.Serializer.Json.Converters;
using NetTopologySuite.IO.Converters;

namespace Tests;

public class SystemJsonSerializerTests
{
    private readonly SystemJsonSerializer _serializer = new(new JsonSerializerOptions());

    [Fact]
    public void serialize_object_should_write_to_stream()
    {
        // given
        var obj = new TestClass { Name = "Test", Age = 30 };
        using var stream = new MemoryStream();

        // when
        _serializer.Serialize(obj, stream);
        stream.Position = 0;
        using var reader = new StreamReader(stream);

        var result = reader.ReadToEnd();

        // then
        result.Should().Be("{\"Name\":\"Test\",\"Age\":30}");
    }

    [Fact]
    public void deserialize_stream_should_return_valid_object()
    {
        // given
        const string json = "{\"Name\":\"Test\",\"Age\":30}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        // when
        var result = _serializer.Deserialize<TestClass>(stream);

        // then
        result.Should().NotBeNull();
        result!.Name.Should().NotBeNullOrWhiteSpace().And.Be("Test");
        result.Age.Should().Be(30);
    }

    [Fact]
    public void serialize_null_object_should_handle_gracefully()
    {
        // given
        object? obj = null;
        using var stream = new MemoryStream();

        // when
        _serializer.Serialize(obj, stream);
        stream.Position = 0;
        using var readerStream = new StreamReader(stream);

        var result = readerStream.ReadToEnd();

        // then
        result.Should().BeNullOrWhiteSpace();
    }

    [Fact]
    public void deserialize_invalid_Json_should_throw_json_exception()
    {
        // given
        const string invalidJson = "{\"Name\":\"Test\",\"Age\":}";

        // when
        var act = () =>
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(invalidJson));
            _serializer.Deserialize<TestClass>(stream);
        };

        // then
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void create_web_json_options_should_include_default_converters()
    {
        // when
        var options = JsonConstants.CreateWebJsonOptions();

        // then
        options.Converters.Should().Contain(c => c is GeoJsonConverterFactory);
        options.Converters.Should().Contain(c => c is IpAddressJsonConverter);
    }

    private sealed class TestClass
    {
        public required string Name { get; init; }

        public required int Age { get; init; }
    }
}
