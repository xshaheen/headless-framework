// Copyright (c) Mahmoud Shaheen. All rights reserved.

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;

namespace Headless.Serializer.Benchmarks;

internal static class BenchmarkRunConfig
{
    private static readonly string _ArtifactsPath = Path.Combine("artifacts", "benchmark", "serializer");

    public static IConfig Create(string[] args)
    {
        // DefaultConfig.Instance already registers the GitHub markdown + HTML exporters; re-adding them triggers
        // a "exporter already present" config warning and duplicate output, so only the diagnoser is added here.
        var config = ManualConfig
            .Create(DefaultConfig.Instance)
            .WithArtifactsPath(_ArtifactsPath)
            .AddDiagnoser(MemoryDiagnoser.Default);

        if (!_HasExplicitJob(args))
        {
            config.AddJob(Job.Default.WithId("buffer-vs-stream"));
        }

        return config;
    }

    private static bool _HasExplicitJob(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg is "-j" or "--job")
            {
                return true;
            }

            if (arg.StartsWith("--job=", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
