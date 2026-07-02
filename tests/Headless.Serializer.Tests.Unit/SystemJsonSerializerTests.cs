// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Serializer;
using Headless.Serializer.Converters;

namespace Tests;

public sealed class SystemJsonSerializerTests
{
    private readonly SystemJsonSerializer _serializer = new();

    [Fact]
    public void serialize_type_should_write_to_stream()
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
        result.Should().Be("{\"name\":\"Test\",\"age\":30}");
    }

    [Fact]
    public void serialize_object_should_write_to_stream()
    {
        // given
        object obj = new TestClass { Name = "Test", Age = 30 };
        using var stream = new MemoryStream();

        // when
        _serializer.Serialize(obj, stream);
        stream.Position = 0;
        using var reader = new StreamReader(stream);

        var result = reader.ReadToEnd();

        // then
        result.Should().Be("{\"name\":\"Test\",\"age\":30}");
    }

    [Fact]
    public void deserialize_stream_should_return_valid_object()
    {
        // given
        const string json = "{\"name\":\"Test\",\"age\":30}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        // when
        var result = _serializer.Deserialize<TestClass>(stream);

        // then
        result.Should().NotBeNull();
        result.Name.Should().NotBeNullOrWhiteSpace().And.Be("Test");
        result.Age.Should().Be(30);
    }

    [Fact]
    public void deserialize_stream_honors_reader_options_from_serializer_options()
    {
        // given — DefaultWebJsonOptions sets AllowTrailingCommas = true; the Stream/sequence path must honor the
        // configured reader options, not fall back to Utf8JsonReader defaults.
        const string json = "{\"name\":\"Test\",\"age\":30,}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        // when
        var result = _serializer.Deserialize<TestClass>(stream);

        // then
        result.Should().NotBeNull();
        result.Name.Should().Be("Test");
        result.Age.Should().Be(30);
    }

    [Fact]
    public void deserialize_stream_reads_from_current_position()
    {
        // given — a payload preceded by a prefix the caller has already consumed; the stream is positioned at the
        // payload start, so deserialization must read from Position, not from offset 0.
        var prefix = "PREFIX"u8.ToArray();
        var payload = Encoding.UTF8.GetBytes("{\"name\":\"Test\",\"age\":30}");
        using var stream = new MemoryStream([.. prefix, .. payload]);
        stream.Position = prefix.Length;

        // when
        var result = _serializer.Deserialize<TestClass>(stream);

        // then
        result.Should().NotBeNull();
        result.Name.Should().Be("Test");
        result.Age.Should().Be(30);
    }

    [Fact]
    public void deserialize_stream_rejects_trailing_content()
    {
        // given — the sequence/Stream path must reject trailing non-whitespace just like the span/byte[] overloads,
        // so a corrupt "{...}<garbage>" payload cannot deserialize silently.
        const string json = "{\"name\":\"Test\",\"age\":30}garbage";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        // when
        var act = () => _serializer.Deserialize<TestClass>(stream);

        // then
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void deserialize_stream_consumes_the_stream()
    {
        // given
        var payload = Encoding.UTF8.GetBytes("{\"name\":\"Test\",\"age\":30}");
        using var stream = new MemoryStream(payload);

        // when
        _ = _serializer.Deserialize<TestClass>(stream);

        // then — the stream is consumed (Position advanced to the end), matching the old Stream API.
        stream.Position.Should().Be(stream.Length);
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
        result.Should().Be("null");
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
        options.Converters.Should().Contain(c => c is IpAddressJsonConverter);
    }

    [Fact]
    public void should_use_custom_options_provider()
    {
        // given
        var customOptions = new JsonSerializerOptions { PropertyNamingPolicy = null };
        var optionsProvider = Substitute.For<IJsonOptionsProvider>();
        optionsProvider.GetSerializeOptions().Returns(customOptions);
        optionsProvider.GetDeserializeOptions().Returns(customOptions);

        var serializer = new SystemJsonSerializer(optionsProvider);
        var obj = new TestClass { Name = "Test", Age = 25 };
        using var stream = new MemoryStream();

        // when
        serializer.Serialize(obj, stream);
        stream.Position = 0;
        using var reader = new StreamReader(stream);
        var result = reader.ReadToEnd();

        // then - PascalCase since PropertyNamingPolicy is null
        result.Should().Be("{\"Name\":\"Test\",\"Age\":25}");
        optionsProvider.Received(1).GetSerializeOptions();
    }

    [Fact]
    public void serialize_honors_custom_indentation_options()
    {
        // given — a custom provider enabling indentation; the buffer write path must honor WriteIndented and the
        // related formatting options, not fall back to the compact Utf8JsonWriter defaults.
        var indentedOptions = JsonConstants.CreateWebJsonOptions();
        indentedOptions.WriteIndented = true;
        var provider = Substitute.For<IJsonOptionsProvider>();
        provider.GetSerializeOptions().Returns(indentedOptions);
        provider.GetDeserializeOptions().Returns(indentedOptions);
        var serializer = new SystemJsonSerializer(provider);

        // when
        var json = serializer.SerializeToString(new TestClass { Name = "Test", Age = 30 });

        // then — indented output spans multiple lines; compact output would be a single line.
        json.Should().NotBeNull();
        json!.Should().Contain("\n");
    }

    [Fact]
    public void should_deserialize_with_type_parameter()
    {
        // given
        const string json = "{\"name\":\"John\",\"age\":42}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        // when
#pragma warning disable CA2263 // Prefer generic
        var result = _serializer.Deserialize<TestClass>(stream);
#pragma warning restore CA2263

        // then
        result.Should().NotBeNull();
        result.Should().BeOfType<TestClass>();
        result.Name.Should().Be("John");
        result.Age.Should().Be(42);
    }

    [Fact]
    public void should_handle_complex_nested_objects()
    {
        // given
        var nested = new NestedClass
        {
            Id = 1,
            Inner = new InnerClass
            {
                Value = "inner-value",
                Deeper = new DeeperClass { Code = "ABC123" },
            },
        };
        using var stream = new MemoryStream();

        // when
        _serializer.Serialize(nested, stream);
        stream.Position = 0;
        var deserialized = _serializer.Deserialize<NestedClass>(stream);

        // then
        deserialized.Should().NotBeNull();
        deserialized!.Id.Should().Be(1);
        deserialized.Inner.Should().NotBeNull();
        deserialized.Inner!.Value.Should().Be("inner-value");
        deserialized.Inner.Deeper.Should().NotBeNull();
        deserialized.Inner.Deeper!.Code.Should().Be("ABC123");
    }

    [Fact]
    public void should_handle_collections()
    {
        // given
        var collection = new CollectionClass
        {
            Items = ["one", "two", "three"],
            Numbers = [1, 2, 3],
            Children = [new TestClass { Name = "Child1", Age = 10 }, new TestClass { Name = "Child2", Age = 20 }],
        };
        using var stream = new MemoryStream();

        // when
        _serializer.Serialize(collection, stream);
        stream.Position = 0;
        var deserialized = _serializer.Deserialize<CollectionClass>(stream);

        // then
        deserialized.Should().NotBeNull();
        deserialized!.Items.Should().BeEquivalentTo(["one", "two", "three"]);
        deserialized.Numbers.Should().BeEquivalentTo([1, 2, 3]);
        deserialized.Children.Should().HaveCount(2);
        deserialized.Children[0].Name.Should().Be("Child1");
        deserialized.Children[1].Name.Should().Be("Child2");
    }

    [Fact]
    public void should_respect_json_attributes()
    {
        // given
        var obj = new AttributedClass
        {
            CustomName = "visible",
            IgnoredProperty = "should-not-appear",
            RegularProperty = "regular",
        };
        using var stream = new MemoryStream();

        // when
        _serializer.Serialize(obj, stream);
        stream.Position = 0;
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();

        // then
        json.Should().Contain("\"custom_name\"");
        json.Should().Contain("\"visible\"");
        json.Should().NotContain("IgnoredProperty");
        json.Should().NotContain("should-not-appear");
        json.Should().Contain("\"regularProperty\"");

        // verify deserialization respects JsonPropertyName
        using var deserializeStream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var deserialized = _serializer.Deserialize<AttributedClass>(deserializeStream);
        deserialized.Should().NotBeNull();
        deserialized!.CustomName.Should().Be("visible");
        deserialized.RegularProperty.Should().Be("regular");
    }

    private sealed class TestClass
    {
        public required string Name { get; init; }

        public required int Age { get; init; }
    }

    private sealed class NestedClass
    {
        public required int Id { get; init; }

        public InnerClass? Inner { get; init; }
    }

    private sealed class InnerClass
    {
        public required string Value { get; init; }

        public DeeperClass? Deeper { get; init; }
    }

    private sealed class DeeperClass
    {
        public required string Code { get; init; }
    }

    private sealed class CollectionClass
    {
        public required string[] Items { get; init; }

        public required List<int> Numbers { get; init; }

        public required List<TestClass> Children { get; init; }
    }

    private sealed class AttributedClass
    {
        [JsonPropertyName("custom_name")]
        public required string CustomName { get; init; }

        [JsonIgnore]
        public string IgnoredProperty { get; set; } = string.Empty;

        public required string RegularProperty { get; init; }
    }
}
