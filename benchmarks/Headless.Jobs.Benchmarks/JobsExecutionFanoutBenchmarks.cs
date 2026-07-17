// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Attributes;

namespace Headless.Jobs.Benchmarks;

public class JobsExecutionFanoutBenchmarks
{
    private Task[] _tasks = null!;

    [Params(1, 3, 6)]
    public int TaskCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _tasks = Enumerable.Repeat(Task.CompletedTask, TaskCount).ToArray();
    }

    [Benchmark(Baseline = true, Description = "Span -> array -> Task.WhenAll")]
    public Task ArrayCopy()
    {
        return Task.WhenAll(_tasks.AsSpan(0, TaskCount).ToArray());
    }

    [Benchmark(Description = "Span -> Task.WhenAll")]
    public Task Span()
    {
        return Task.WhenAll(_tasks.AsSpan(0, TaskCount));
    }
}
