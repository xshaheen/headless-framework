// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Running;
using Headless.EntityFramework.Benchmarks;

if (args.Contains("--save-pipeline-probe", StringComparer.Ordinal))
{
    await SavePipelineProbe.RunAsync().ConfigureAwait(false);
    return;
}

BenchmarkSwitcher
    .FromTypes([typeof(NavigationTrackingBenchmarks), typeof(SavePipelineBenchmarks), typeof(AuditedInsertBenchmarks)])
    .Run(args, BenchmarkRunConfig.Create());
