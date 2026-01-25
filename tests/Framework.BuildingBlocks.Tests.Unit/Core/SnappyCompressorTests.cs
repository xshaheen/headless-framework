// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Serialization;
using Framework.Abstractions;
using Framework.Core;

namespace Tests.Core;

public sealed partial class SnappyCompressorTests
{
    private sealed record SnappyCompressorEntityTest(int Id, string Name, DateTimeOffset DateCreated);

    private sealed record NestedObject(
        int Id,
        string Name,
        AddressInfo Address,
        List<OrderItem> Orders
    );

    private sealed record AddressInfo(string Street, string City, string Country);

    private sealed record OrderItem(int OrderId, decimal Price, List<string> Tags);

    private sealed record LargeDataItem(int Index, string Data, Guid Identifier);

    private readonly Clock _clock;

    public SnappyCompressorTests()
    {
        var sutTimeProvider = Substitute.For<TimeProvider>();
        sutTimeProvider.GetUtcNow().Returns(new DateTimeOffset(2024, 11, 27, 12, 0, 0, TimeSpan.Zero));
        sutTimeProvider.LocalTimeZone.Returns(TimeZoneInfo.Local);
        _clock = new Clock(sutTimeProvider);
    }

    [Fact]
    public void compress_should_compress_and_return_valid_memory()
    {
        // given
        var testObject = new SnappyCompressorEntityTest(1, "Framework", _clock.UtcNow);
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        // when
        var compressedData = SnappyCompressor.Compress(testObject, jsonOptions);

        // then
        compressedData.Length.Should().BePositive();
    }

    [Fact]
    public void decompress_should_return_original_object_from_compressed_data()
    {
        // given
        var testObject = new SnappyCompressorEntityTest(1, "Framework", _clock.UtcNow);
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var compressedData = SnappyCompressor.Compress(testObject, jsonOptions);

        // when
        var decompressedObject = SnappyCompressor.Decompress<SnappyCompressorEntityTest>(compressedData, jsonOptions);

        // then
        decompressedObject.Should().NotBeNull();
        decompressedObject.Id.Should().Be(1);
        decompressedObject.Name.Should().Be("Framework");
        decompressedObject.DateCreated.Should().Be(_clock.UtcNow);
    }

    [Fact]
    public void should_compress_and_decompress_complex_object()
    {
        // given
        var testObject = new NestedObject(
            Id: 42,
            Name: "Test Order",
            Address: new AddressInfo("123 Main St", "Seattle", "USA"),
            Orders:
            [
                new OrderItem(1, 99.99m, ["electronics", "sale"]),
                new OrderItem(2, 49.50m, ["books"]),
            ]
        );

        // when
        var compressed = SnappyCompressor.Compress(testObject);
        var decompressed = SnappyCompressor.Decompress<NestedObject>(compressed);

        // then
        decompressed.Should().NotBeNull();
        decompressed!.Id.Should().Be(42);
        decompressed.Name.Should().Be("Test Order");
        decompressed.Address.Street.Should().Be("123 Main St");
        decompressed.Address.City.Should().Be("Seattle");
        decompressed.Orders.Should().HaveCount(2);
        decompressed.Orders[0].Tags.Should().BeEquivalentTo(["electronics", "sale"]);
    }

    [Fact]
    public void should_use_custom_json_options()
    {
        // given
        var testObject = new SnappyCompressorEntityTest(1, "Framework", _clock.UtcNow);
        var customOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        // when
        var compressed = SnappyCompressor.Compress(testObject, customOptions);
        var decompressed = SnappyCompressor.Decompress<SnappyCompressorEntityTest>(compressed, customOptions);

        // then
        decompressed.Should().NotBeNull();
        decompressed!.Id.Should().Be(1);
        decompressed.Name.Should().Be("Framework");
    }

    [Fact]
    public void should_handle_null_value()
    {
        // given
        SnappyCompressorEntityTest? testObject = null;

        // when
        var compressed = SnappyCompressor.Compress(testObject);
        var decompressed = SnappyCompressor.Decompress<SnappyCompressorEntityTest>(compressed);

        // then
        decompressed.Should().BeNull();
    }

    [Fact]
    public void should_handle_empty_object()
    {
        // given
        var testObject = new Dictionary<string, object>(StringComparer.Ordinal);

        // when
        var compressed = SnappyCompressor.Compress(testObject);
        var decompressed = SnappyCompressor.Decompress<Dictionary<string, object>>(compressed);

        // then
        decompressed.Should().NotBeNull();
        decompressed.Should().BeEmpty();
    }

    [Fact]
    public void should_handle_large_object()
    {
        // given
        var largeList = Enumerable.Range(0, 1000)
            .Select(i => new LargeDataItem(i, $"Data item number {i} with some padding text", Guid.NewGuid()))
            .ToList();

        // when
        var compressed = SnappyCompressor.Compress(largeList);
        var decompressed = SnappyCompressor.Decompress<List<LargeDataItem>>(compressed);

        // then
        decompressed.Should().NotBeNull();
        decompressed.Should().HaveCount(1000);
        decompressed![0].Index.Should().Be(0);
        decompressed[999].Index.Should().Be(999);
    }

    [Fact]
    public void should_compress_with_json_type_info()
    {
        // given
        var testObject = new SnappyCompressorEntityTest(1, "Framework", _clock.UtcNow);

        // when
        var compressed = SnappyCompressor.Compress(testObject, SnappyTestJsonContext.Default.SnappyCompressorEntityTest);

        // then
        compressed.Length.Should().BePositive();
    }

    [Fact]
    public void should_decompress_with_json_type_info()
    {
        // given
        var testObject = new SnappyCompressorEntityTest(1, "Framework", _clock.UtcNow);
        var compressed = SnappyCompressor.Compress(testObject, SnappyTestJsonContext.Default.SnappyCompressorEntityTest);

        // when
        var decompressed = SnappyCompressor.Decompress(compressed, SnappyTestJsonContext.Default.SnappyCompressorEntityTest);

        // then
        decompressed.Should().NotBeNull();
        decompressed!.Id.Should().Be(1);
        decompressed.Name.Should().Be("Framework");
        decompressed.DateCreated.Should().Be(_clock.UtcNow);
    }

    // Source generator context for AOT-compatible JSON serialization
    [JsonSerializable(typeof(SnappyCompressorEntityTest))]
    private sealed partial class SnappyTestJsonContext : JsonSerializerContext;
}
