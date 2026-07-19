// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;

namespace Headless.Jobs.Benchmarks;

internal static class BenchmarkRunConfig
{
    public static IConfig Create(string[] args)
    {
        var config = ManualConfig
            .Create(DefaultConfig.Instance)
            .WithArtifactsPath(Path.Combine("artifacts", "benchmark", "jobs"))
            .AddDiagnoser(MemoryDiagnoser.Default);

        if (!args.Any(arg => arg is "-j" or "--job" || arg.StartsWith("--job=", StringComparison.Ordinal)))
        {
            config.AddJob(Job.Default.WithId("typed-request-read"));
        }

        return config;
    }
}
