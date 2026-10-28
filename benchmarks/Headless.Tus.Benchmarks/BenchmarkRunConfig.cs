// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Jobs;

namespace Headless.Tus.Benchmarks;

internal static class BenchmarkRunConfig
{
    public static IConfig Create(string[] args)
    {
        var config = ManualConfig
            .Create(DefaultConfig.Instance)
            .WithArtifactsPath(Path.Combine("artifacts", "benchmark", "Tus"))
            .AddDiagnoser(MemoryDiagnoser.Default);

        if (!args.Any(arg => arg is "-j" or "--job" || arg.StartsWith("--job=", StringComparison.Ordinal)))
        {
            // One upload moves 100 MiB through a container, so each invocation takes seconds: measure single
            // invocations instead of letting the default job's pilot stage try to batch them.
            config.AddJob(Job.Default.WithStrategy(RunStrategy.Monitoring).WithIterationCount(10).WithId("upload"));
        }

        return config;
    }
}
