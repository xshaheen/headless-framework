// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching.Benchmarks;

namespace Tests;

public sealed class BenchmarkPayloadFactoryTests
{
    [Fact]
    public void Create_WithSameSizeAndSeed_ReturnsDeterministicPayload()
    {
        var first = BenchmarkPayloadFactory.Create(128, seed: 42);
        var second = BenchmarkPayloadFactory.Create(128, seed: 42);

        first.Id.Should().Be(second.Id);
        first.Text.Should().Be(second.Text);
        first.Bytes.Should().Equal(second.Bytes);
    }

    [Fact]
    public void Create_WithNonPositiveSize_Throws()
    {
        Action act = () => BenchmarkPayloadFactory.Create(0, seed: 42);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("sizeBytes");
    }
}
