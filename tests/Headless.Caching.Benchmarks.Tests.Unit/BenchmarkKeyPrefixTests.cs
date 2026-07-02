// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching.Benchmarks;

namespace Tests;

public sealed class BenchmarkKeyPrefixTests
{
    [Fact]
    public void Create_SanitizesSegmentsAndAddsTrailingSeparator()
    {
        var prefix = BenchmarkKeyPrefix.Create("Fusion Cache", "Hot Path", "Run01");

        prefix.Should().Be("bench:fusion-cache:hot-path:run01:");
    }

    [Fact]
    public void Create_WithBlankProvider_Throws()
    {
        Action act = () => BenchmarkKeyPrefix.Create("", "scenario", "run");

        act.Should().Throw<ArgumentException>().WithParameterName("providerId");
    }
}
