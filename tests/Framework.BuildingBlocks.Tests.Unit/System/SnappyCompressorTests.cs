// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.BuildingBlocks.Abstractions;
using Framework.BuildingBlocks.System;

namespace Tests.System;

public class SnappyCompressorTests
{
    private sealed record SnappyCompressorEntityTest(int Id, string Name, DateTimeOffset DateCreated);

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
        decompressedObject!.Id.Should().Be(1);
        decompressedObject.Name.Should().Be("Framework");
        decompressedObject.DateCreated.Should().Be(_clock.UtcNow);
    }
}
