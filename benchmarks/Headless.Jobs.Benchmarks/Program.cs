// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Running;
using Headless.Jobs.Benchmarks;

if (args.Contains("--scheduler-latency-probe", StringComparer.Ordinal))
{
    await JobsTaskSchedulerBenchmarks.RunLatencyProbeAsync().ConfigureAwait(false);
    return;
}

BenchmarkSwitcher
    .FromTypes([
        typeof(JobsRequestSerializationBenchmarks),
        typeof(JobsExecutionFanoutBenchmarks),
        typeof(JobsTaskSchedulerBenchmarks),
    ])
    .Run(args, BenchmarkRunConfig.Create(args));
