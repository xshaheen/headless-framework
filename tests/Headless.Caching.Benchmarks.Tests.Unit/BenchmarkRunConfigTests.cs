// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching.Benchmarks;

namespace Tests;

public sealed class BenchmarkRunConfigTests
{
    [Fact]
    public void create_without_job_arguments_adds_default_cache_comparison_job()
    {
        var config = BenchmarkRunConfig.Create([]);

        config.GetJobs().Should().ContainSingle(x => x.Id == "cache-comparison");
    }

    [Theory]
    [InlineData("-j", "Dry")]
    [InlineData("--job", "Dry")]
    [InlineData("--job=Dry")]
    public void create_with_job_arguments_does_not_add_default_cache_comparison_job(params string[] args)
    {
        var config = BenchmarkRunConfig.Create(args);

        config.GetJobs().Should().NotContain(x => x.Id == "cache-comparison");
    }
}
