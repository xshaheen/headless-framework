// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Serializer.Converters;

namespace Tests.Converters;

public sealed class CollectionItemJsonConverterTests
{
    private readonly JsonSerializerOptions _options;

    public CollectionItemJsonConverterTests()
    {
        _options = new JsonSerializerOptions();
        _options.Converters.Add(new CollectionItemJsonConverter<string, SampleConverter>());
    }

    [Fact]
    public void json_collection_item_converter_should_deserialize_valid_collection()
    {
        // given
        const string json = "[\"item1\", \"item2\", \"item3\"]";

        // when
        var result = JsonSerializer.Deserialize<IEnumerable<string>>(json, _options)?.ToList();

        // then
        result.Should().NotBeNull();
        result.Should().HaveCount(3);
        result.Should().ContainInOrder("item1", "item2", "item3");
    }

    [Fact]
    public void json_collection_item_converter_should_serialize_valid_collection()
    {
        // given
        var items = new List<string> { "item1", "item2", "item3" };

        // when
        var result = JsonSerializer.Serialize(items, _options);

        // then
        result.Should().Be("[\"item1\",\"item2\",\"item3\"]");
    }

    [Fact]
    public void json_collection_item_converter_should_return_null_for_null_json()
    {
        // given
        const string json = "null";

        // when
        var result = JsonSerializer.Deserialize<IEnumerable<string>>(json, _options);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public void json_collection_item_converter_should_throw_on_invalid_json()
    {
        // given
#pragma warning disable JSON001 // Invalid JSON pattern
        const string json = "[\"item1\", \"item2\", \"item3\"";
#pragma warning restore JSON001 // Invalid JSON pattern

        // when
        Action act = () => JsonSerializer.Deserialize<IEnumerable<string>>(json, _options);

        // then
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void json_collection_item_converter_should_serialize_empty_collection()
    {
        // given
        var items = new List<string>();

        // when
        var result = JsonSerializer.Serialize(items, _options);

        // then
        result.Should().Be("[]");
    }

    [Fact]
    public void json_collection_item_converter_should_deserialize_with_custom_object_converter()
    {
        // given
        const string json = "[{\"Name\":\"John\"}]";

        // when
        var result = JsonSerializer.Deserialize<IEnumerable<SampleType>>(json, _options)?.ToList();

        // then
        result.Should().NotBeNull();
        result.Should().ContainSingle();
        result[0].Name.Should().Be("John");
    }

    [Fact]
    public void json_collection_item_converter_should_handle_empty_collection()
    {
        // given
        const string json = "[]";

        // when
        var result = JsonSerializer.Deserialize<IEnumerable<string>>(json, _options)?.ToList();

        // then
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    private sealed class SampleType
    {
        public string? Name { get; init; }
    }

    [PublicAPI]
    private sealed class SampleConverter : JsonConverter<SampleType>
    {
        public override SampleType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);

            return new SampleType { Name = doc.RootElement.GetString() };
        }

        public override void Write(Utf8JsonWriter writer, SampleType value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.Name);
        }
    }
}
