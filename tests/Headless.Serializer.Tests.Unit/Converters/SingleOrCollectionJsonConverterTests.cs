// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Serializer.Converters;

#pragma warning disable JSON001 // Invalid JSON pattern
namespace Tests.Converters;

public sealed class SingleOrCollectionJsonConverterTests
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        Converters = { new SingleOrListJsonConverter<MyItem>(), new SingleOrHashsetJsonConverter<MyItem>() },
    };

    [Fact]
    public void should_deserialize_single_item_to_list_when_single_or_collection_converter()
    {
        // given
        const string json = "{\"Id\": 1, \"Name\": \"Item 1\"}";

        // when
        var result = JsonSerializer.Deserialize<List<MyItem>>(json, _jsonOptions);

        // then
        result.Should().NotBeNull();
        result.Should().ContainSingle();
        result[0].Id.Should().Be(1);
        result[0].Name.Should().Be("Item 1");
    }

    [Fact]
    public void should_deserialize_array_to_list_when_single_or_collection_converter()
    {
        // given
        const string json = "[{\"Id\": 1, \"Name\": \"Item 1\"}, {\"Id\": 2, \"Name\": \"Item 2\"}]";

        // when
        var result = JsonSerializer.Deserialize<List<MyItem>>(json, _jsonOptions);

        // then
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result[0].Id.Should().Be(1);
        result[0].Name.Should().Be("Item 1");
        result[1].Id.Should().Be(2);
        result[1].Name.Should().Be("Item 2");
    }

    [Fact]
    public void should_return_null_for_null_json_when_single_or_collection_converter()
    {
        // given
        const string json = "null";

        // when
        var result = JsonSerializer.Deserialize<List<MyItem>>(json, _jsonOptions);

        // then
        result.Should().BeNull();
    }

    [Fact]
    public void should_throw_exception_for_invalid_json_when_single_or_collection_converter()
    {
        // given
        const string json = "[{\"Id\": 1, \"Name\": \"Item 1\"}, {\"Id\": 2}";

        // when
        Action act = () => JsonSerializer.Deserialize<List<MyItem>>(json, _jsonOptions);

        // then
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void should_deserialize_to_hashset_when_single_or_collection_converter()
    {
        // given
        const string json = "[{\"Id\": 1, \"Name\": \"Item 1\"}, {\"Id\": 2, \"Name\": \"Item 2\"}]";

        // when
        var result = JsonSerializer.Deserialize<HashSet<MyItem>>(json, _jsonOptions);

        // then
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result.Should().ContainEquivalentOf(new MyItem { Id = 1, Name = "Item 1" });
        result.Should().ContainEquivalentOf(new MyItem { Id = 2, Name = "Item 2" });
    }

    [Fact]
    public void should_deserialize_single_item_as_list_in_array_when_single_or_collection_converter()
    {
        // given
        const string json = "[{\"Id\": 1, \"Name\": \"Item 1\"}]";

        // when
        var result = JsonSerializer.Deserialize<List<MyItem>>(json, _jsonOptions);

        // then
        result.Should().NotBeNull();
        result.Should().ContainSingle();
        result[0].Id.Should().Be(1);
        result[0].Name.Should().Be("Item 1");
    }

    [Fact]
    public void should_handle_unexpected_string_value_when_single_or_collection_converter()
    {
        // given
        const string json = "\"Invalid String\"";

        // when
        Action act = () => JsonSerializer.Deserialize<List<MyItem>>(json, _jsonOptions);

        // then
        act.Should().Throw<JsonException>();
    }

    private sealed class MyItem
    {
        public int Id { get; init; }

        public string? Name { get; init; }
    }
}
