// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching.Benchmarks;

namespace Tests;

public sealed class BenchmarkRunConfigTests
{
    [Fact]
    public void Create_WithoutJobArguments_AddsDefaultCacheComparisonJob()
    {
        var config = BenchmarkRunConfig.Create([]);

        config.GetJobs().Should().ContainSingle(x => x.Id == "cache-comparison");
    }

    [Theory]
    [InlineData("-j", "Dry")]
    [InlineData("--job", "Dry")]
    [InlineData("--job=Dry")]
    public void Create_WithJobArguments_DoesNotAddDefaultCacheComparisonJob(params string[] args)
    {
        var config = BenchmarkRunConfig.Create(args);

        config.GetJobs().Should().NotContain(x => x.Id == "cache-comparison");
    }
}
