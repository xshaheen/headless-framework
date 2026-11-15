// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;

namespace Headless.Sequences.Benchmarks;

internal static class BenchmarkRunConfig
{
    public static IConfig Create(string[] args)
    {
        // Op/s is the figure fast-mode numbering is judged by: calls per second across all concurrent callers.
        var config = ManualConfig
            .Create(DefaultConfig.Instance)
            .WithArtifactsPath(Path.Combine("artifacts", "benchmark", "Sequences"))
            .AddDiagnoser(MemoryDiagnoser.Default)
            .AddColumn(StatisticColumn.OperationsPerSecond);

        if (!args.Any(arg => arg is "-j" or "--job" || arg.StartsWith("--job=", StringComparison.Ordinal)))
        {
            config.AddJob(Job.Default.WithId("next"));
        }

        return config;
    }
}
