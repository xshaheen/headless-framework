// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching.Benchmarks;

namespace Tests;

public sealed class BenchmarkKeyPrefixTests
{
    [Fact]
    public void create_sanitizes_segments_and_adds_trailing_separator()
    {
        var prefix = BenchmarkKeyPrefix.Create("Fusion Cache", "Hot Path", "Run01");

        prefix.Should().Be("bench:fusion-cache:hot-path:run01:");
    }

    [Fact]
    public void create_with_blank_provider_throws()
    {
        Action act = () => BenchmarkKeyPrefix.Create("", "scenario", "run");

        act.Should().Throw<ArgumentException>().WithParameterName("providerId");
    }
}
